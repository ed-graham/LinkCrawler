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

namespace LinkCrawler
{
    public class LinkCrawler
    {
        public string BaseUrl { get; set; }
        public bool CheckImages { get; set; }
        public bool FollowRedirects { get; set; }
        private bool NeedToLogIn { get; set; }
        private string AuthLoginUrl { get; set; }
        private string AuthLoginPostBody { get; set; }

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
            AuthLoginUrl = settings.AuthLoginUrl ?? "";
            AuthLoginPostBody = settings.AuthLoginPostBody ?? "";
            NeedToLogIn = settings.AuthLoginUrl != null;

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
            UrlList.Add(new LinkModel(BaseUrl));
            SendRequest(BaseUrl);
        }

        public void SendRequest(string crawlUrl, string referrerUrl = "")
        {
            var requestModel = new RequestModel(crawlUrl, referrerUrl, BaseUrl);
            Client.BaseUrl = new Uri(crawlUrl);
            if (NeedToLogIn && crawlUrl.StartsWith(BaseUrl + AuthLoginUrl))
            {
                NeedToLogIn = false; // only try this once, in case we get stuck in an infinite loop of errors
                Console.WriteLine("***HOLY SHIT!!***");
                Console.WriteLine("***HOLY SHIT!!***");
                Console.WriteLine("***HOLY SHIT!!***");
                Console.WriteLine("***HOLY SHIT!!***");
                Console.WriteLine("***HOLY SHIT!!***");
                PostRequest = new RestRequest(Method.POST).AddBody(AuthLoginPostBody) as RestRequest;
                Client.ExecuteAsync(PostRequest, response =>
                {
                    if (response == null)
                        return;

                    var responseModel = new ResponseModel(response, requestModel, _settings);
                    ProcessResponse(responseModel);
                });
            }
            else
            {
                Client.ExecuteAsync(GetRequest, response =>
                {
                    if (response == null)
                        return;

                    var responseModel = new ResponseModel(response, requestModel, _settings);
                    ProcessResponse(responseModel);
                });
            }
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

            SendRequest(redirectUrl, responseModel.RequestedUrl);
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
                lock (UrlList)
                {
                    if (UrlList.Where(l => l.Address == url).Count() > 0)
                        continue;

                    UrlList.Add(new LinkModel(url));
                }
                SendRequest(url, referrerUrl);
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