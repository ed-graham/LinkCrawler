using LinkCrawler.Utils;
using StructureMap;
using System;
using LinkCrawler.Utils.Parsers;
using LinkCrawler.Utils.Settings;
using LinkCrawler.Utils.Extensions;

namespace LinkCrawler
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var container = Container.For<StructureMapRegistry>())
            {
                var linkCrawler = container.GetInstance<LinkCrawler>();
                if (args.Length > 0)
                {
                    // Base URL
                    var validUrlParser = new ValidUrlParser(new Settings());
                    var result = validUrlParser.Parse(args[0], out string parsed);
                    if (result)
                    {
                        // make sure the base URL is just a domain
                        linkCrawler.BaseUrl = validUrlParser.BaseUrl = parsed.GetUrlBase();
                        linkCrawler.ValidUrlParser = validUrlParser;
                    }
                    if (args.Length > 3)
                    {
                        // log-in URL
                        string loginUrl = args[1];
                        linkCrawler.LoginUrl = (loginUrl.EndsWith("/")) ? loginUrl.Substring(0, loginUrl.Length - 1) : loginUrl;
                        // user-name
                        linkCrawler.UserName = args[2];
                        // password
                        linkCrawler.Password = args[3];
                        if (args.Length > 4)
                        {
                            // log-out URL: need to avoid it otherwise we'll need to log in again!
                            string logoutUrl = args[4];
                            linkCrawler.LogoutUrl = (logoutUrl.EndsWith("/")) ? logoutUrl.Substring(0, logoutUrl.Length - 1) : logoutUrl;
                            if (args.Length > 5)
                            {
                                // excluded URL stem
                                linkCrawler.ExcludedUrlStem = args[5];
                            }
                        }
                    }
                }
                linkCrawler.Start();
                // this line *has* to be here, because otherwise the app finishes before the asynchronous HTTP requests have returned
                Console.Read();
            }
        }
    }
}
