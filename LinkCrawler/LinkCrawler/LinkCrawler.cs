using LinkCrawler.Models;
using LinkCrawler.Utils.Extensions;
using LinkCrawler.Utils.Helpers;
using LinkCrawler.Utils.Outputs;
using LinkCrawler.Utils.Parsers;
using LinkCrawler.Utils.Settings;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;

namespace LinkCrawler
{
    public class LinkCrawler
    {
        public string BaseUrl { get; set; }
        public bool CheckImages { get; set; }
        public bool FollowRedirects { get; set; }
        public string LoginUrl { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string LogoutUrl { get; set; }
        public string ExcludedUrlStem { get; set; }

        private RestRequest GetRequest { get; set; }
        private RestRequest PostRequest { get; set; }
        private RestClient Client{ get; set; }
        public IEnumerable<IOutput> Outputs { get; set; }
        public IValidUrlParser ValidUrlParser { get; set; }
        public bool OnlyReportBrokenLinksToOutput { get; set; }
        public List<LinkModel> UrlList;
        private readonly ISettings _settings;
        private readonly Stopwatch timer;
        
        public LinkCrawler(IEnumerable<IOutput> outputs, IValidUrlParser validUrlParser, ISettings settings)
        {
            BaseUrl = settings.BaseUrl;
            Outputs = outputs;
            ValidUrlParser = validUrlParser;
            CheckImages = settings.CheckImages;
            FollowRedirects = settings.FollowRedirects;

            UrlList = new List<LinkModel>();
            GetRequest = new RestRequest(Method.GET).SetHeader("Accept", "*/*");
            Client = new RestClient() { FollowRedirects = false }; // we don't want RestSharp following the redirects, otherwise we won't see them
            // https://stackoverflow.com/questions/8823349/how-do-i-use-the-cookie-container-with-restsharp-and-asp-net-sessions - set cookies up according to this link?
            Client.CookieContainer = new CookieContainer(); // yup, that's it right there - nothing else to say
            OnlyReportBrokenLinksToOutput = settings.OnlyReportBrokenLinksToOutput;
            _settings = settings;
            this.timer = new Stopwatch();
        }

        public void Start()
        {
            this.timer.Start();
            // first check if we need to log in at some point - if so, get it out of the way now
            if (!String.IsNullOrWhiteSpace(LoginUrl))
            {
                LogInToSite();
            }

            UrlList.Add(new LinkModel(BaseUrl));
            SendRequest(BaseUrl);
        }

        private void LogInToSite()
        {
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine($"***Getting the log-in page*** ");
            // first, get the log-in page and associated cookies
            string loginUrl = BaseUrl + LoginUrl;
            var requestModel = new RequestModel(loginUrl, "", BaseUrl);
            Client.BaseUrl = new Uri(loginUrl);
            // *synchronous* HTTP GET
            IRestResponse getResponse = Client.Execute(GetRequest);
            // all cookies are automatically stored thanks to the "Client.CookieContainer = new CookieContainer();" line above - cool or what?
            PrintCookies();
            // then parse the request verification token if present in a (hidden) input field
            string requestVerificationToken = "";
            string regExSeparator = @"\s+(.*\s+)?";
            Match rvtMatch = Regex.Match(getResponse.Content, $"<input{regExSeparator}name=\"__RequestVerificationToken\"{regExSeparator}value=\"(?<rvt>.+)\"", RegexOptions.IgnoreCase);
            if (rvtMatch.Success) requestVerificationToken = rvtMatch.Groups["rvt"].Value;
            Console.WriteLine($"Form value\t__RequestVerificationToken\t{requestVerificationToken}");
            // finally, post the log-in details back to the log-in page (with the __RequestVerificationToken cookie and corresponding hidden field if they are present)
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine($"***Posting the credentials back to the server*** ");
            PostRequest = new RestRequest(Method.POST).SetHeader("Content-Type", "application/x-www-form-urlencoded");
            // add body
            string body = (String.IsNullOrWhiteSpace(requestVerificationToken) ? "" : $"__RequestVerificationToken={requestVerificationToken}&") +
                        // note that the user-name and password must be URL-encoded as they are part of a form response
                        $"frmLoginEmail={HttpUtility.UrlEncode(UserName)}&frmLoginPass={HttpUtility.UrlEncode(Password)}&frmReturnUrl=%2F";
            Console.WriteLine($"POST body\t{body}");
            //PostRequest.AddBody(body); // doesn't work -- results in just <String /> (10 chars) being sent as the message body
            PostRequest.AddParameter("application/x-www-form-urlencoded", body, ParameterType.RequestBody);
            // *synchronous* HTTP POST
            IRestResponse postResponse = Client.Execute(PostRequest);
            PrintCookies();
        }

        private void PrintCookies()
        {
            Console.WriteLine();
            Console.WriteLine("**Cookies**");
            Console.WriteLine();
            foreach (Cookie cookie in Client.CookieContainer.GetCookies(new Uri(BaseUrl)))
            {
                Console.WriteLine($"Cookie\t{cookie.Name}\t{cookie.Value}");
            }
            Console.WriteLine();
        }

        private void IsApplicationCookiePresent()
        {
            bool present = false;
            foreach (Cookie cookie in Client.CookieContainer.GetCookies(new Uri(BaseUrl)))
            {
                if (cookie.Name.Contains("ApplicationCookie"))
                {
                    present = true;
                    break;
                }
            }
            Console.WriteLine($"Application cookie: {present}");
        }

        public void SendRequest(string crawlUrl, string referrerUrl = "")
        {
            var requestModel = new RequestModel(crawlUrl, referrerUrl, BaseUrl);
            Client.BaseUrl = new Uri(crawlUrl);
            // HTTP GET
            Client.ExecuteAsync(GetRequest, response =>
            {
                if (response == null)
                    return;

                var responseModel = new ResponseModel(response, requestModel, _settings);
                ProcessResponse(responseModel);
            });
        }

        public void ProcessResponse(IResponseModel responseModel)
        {
            WriteOutput(responseModel);

            // follow 3xx redirects
            if (FollowRedirects && responseModel.IsRedirect)
                FollowRedirect(responseModel);

            // follow internal links in response
            if (responseModel.ShouldCrawl)
                CrawlLinksInResponse(responseModel);
        }

        private void FollowRedirect(IResponseModel responseModel)
        {
            string redirectUrl;
            if (responseModel.Location.StartsWith("/"))
                redirectUrl = responseModel.RequestedUrl.GetUrlBase() + responseModel.Location; // add base URL to relative links
            else
                redirectUrl = responseModel.Location;

            SendRequestsToLinks(new List<string> { redirectUrl }, responseModel.RequestedUrl);
        }

        public void CrawlLinksInResponse(IResponseModel responseModel)
        {
            var linksFoundInMarkup = MarkupHelpers.GetValidUrlListFromMarkup(responseModel.Markup, ValidUrlParser, CheckImages);

            SendRequestsToLinks(linksFoundInMarkup, responseModel.RequestedUrl);
        }

        private void SendRequestsToLinks(List<string> urls, string referrerUrl)
        {
            foreach (string url in urls)
            {
                // remove trailing /s so's not to crawl any of them twice
                string noSlashUrl = (url.EndsWith("/")) ? url.Substring(0, url.Length - 1) : url;
                // skip log-out URL, if any
                if (!String.IsNullOrWhiteSpace(LogoutUrl) && noSlashUrl.ToLower().StartsWith((BaseUrl + LogoutUrl).ToLower())) continue;
                // skip excluded URLs, if any
                if (!String.IsNullOrWhiteSpace(ExcludedUrlStem) && noSlashUrl.ToLower().StartsWith((BaseUrl + ExcludedUrlStem).ToLower())) continue;

                lock (UrlList)
                {
                    if (UrlList.Where(l => l.Address == noSlashUrl).Count() > 0)
                        continue;

                    UrlList.Add(new LinkModel(noSlashUrl));
                }
                SendRequest(noSlashUrl, referrerUrl);
            }
        }

        public void WriteOutput(IResponseModel responseModel)
        {
            if (responseModel.IsInteresting)
            {
                if (!responseModel.IsSuccess)
                {
                    foreach (var output in Outputs)
                    {
                        output.WriteError(responseModel);
                    }
                }
                else if (!OnlyReportBrokenLinksToOutput)
                {
                    foreach (var output in Outputs)
                    {
                        IsApplicationCookiePresent();
                        output.WriteInfo(responseModel);
                    }
                }
            }

            CheckIfFinal(responseModel);
        }

        private void CheckIfFinal(IResponseModel responseModel)
        {
            lock (UrlList)
            {

                // First set the status code for the completed link (this will set "CheckingFinished" to true)
                foreach (LinkModel lm in UrlList.Where(l => l.Address == responseModel.RequestedUrl))
                {
                    lm.StatusCode = responseModel.StatusCodeNumber;
                }

                // Then check to see whether there are any pending links left to check
                if ((UrlList.Count > 1) && (UrlList.Where(l => l.CheckingFinished == false).Count() == 0))
                {
                    FinaliseSession();
                }
            }
        }

        private void FinaliseSession()
        {
            this.timer.Stop();
            if (this._settings.PrintSummary)
            {
                List<string> messages = new List<string>();
                messages.Add(""); // add blank line to differentiate summary from main output

                messages.Add("Processing complete. Checked " + UrlList.Count() + " links in " + this.timer.ElapsedMilliseconds.ToString() + "ms");

                messages.Add("");
                messages.Add(" Status | # Links");
                messages.Add(" -------+--------");

                IEnumerable<IGrouping<int, string>> StatusSummary = UrlList.GroupBy(link => link.StatusCode, link => link.Address);
                foreach(IGrouping<int,string> statusGroup in StatusSummary)
                {
                    messages.Add(String.Format("   {0}  | {1,5}", statusGroup.Key, statusGroup.Count()));
                }

                foreach (var output in Outputs)
                {
                    output.WriteInfo(messages.ToArray());
                }
            }
        }
    }
}