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
                        linkCrawler.LoginUrl = args[1];
                        // user-name
                        linkCrawler.UserName = args[2];
                        // password
                        linkCrawler.Password = args[3];
                        if (args.Length > 4)
                        {
                            // log-out URL: need to avoid it otherwise we'll need to log in again!
                            linkCrawler.LogoutUrl = args[4];
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
