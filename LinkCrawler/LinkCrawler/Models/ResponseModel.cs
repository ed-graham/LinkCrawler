using LinkCrawler.Utils.Extensions;
using RestSharp;
using System;
using System.Net;
using LinkCrawler.Utils.Settings;
using System.Collections.Generic;

namespace LinkCrawler.Models
{
    public class ResponseModel : IResponseModel
    {
        public string Markup { get; }
        public string RequestedUrl { get; }
        public string ReferrerUrl { get; }
        public string Location { get; }
        private IList<RestResponseCookie> Cookies { get; }

        public HttpStatusCode StatusCode { get; }
        public int StatusCodeNumber { get { return (int)StatusCode; } }
        public bool IsSuccess { get; }
        public bool IsInteresting { get; }
        public bool IsRedirect { get; }
        public bool ShouldCrawl { get; }
        private string ErrorMessage { get; }

        public ResponseModel(IRestResponse restResponse, RequestModel requestModel, ISettings settings)
        {
            ReferrerUrl = requestModel.ReferrerUrl;
            StatusCode = restResponse.StatusCode;
            RequestedUrl = requestModel.Url;
            Location = restResponse.GetHeaderByName("Location"); // returns null if no Location header present in the response
            ErrorMessage = restResponse.ErrorMessage;
            Cookies = restResponse.Cookies;

            IsSuccess = settings.IsSuccess(StatusCode);
            IsInteresting = settings.IsInteresting(StatusCode);
            IsRedirect = settings.IsRedirect(StatusCode);

            if (!IsSuccess)
                return;

            Markup = restResponse.Content;
            ShouldCrawl = IsSuccess && requestModel.IsInternalUrl && restResponse.IsHtmlDocument();
        }

        public override string ToString()
        {
            // cater for HTTP "999" codes returned
            string statusCode = StatusCodeNumber == 999 ? "[Request denied]" : StatusCode.ToString();
            string output = $"{StatusCodeNumber}\t{statusCode}\t{RequestedUrl}";

            if (!IsSuccess)
            {
                if (!String.IsNullOrEmpty(ErrorMessage))
                {
                    return $"{output}{Environment.NewLine}\tError:\t{ErrorMessage}{Environment.NewLine}\tReferer:\t{ReferrerUrl}";
                }
                else
                {
                    return $"{output}{Environment.NewLine}\tReferer:\t{ReferrerUrl}";
                }
            }
            else
            {
                if (!String.IsNullOrEmpty(Location))
                {
                    return $"{output}{Environment.NewLine}\t->\t{Location}";
                }
                else
                {
                    return $"{output}";
                }
            }
        }
    }
}