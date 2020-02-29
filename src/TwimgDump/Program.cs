using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TwimgDump
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var userScreenName = (string?)args.ElementAtOrDefault(0);
            if (userScreenName is null)
            {
                Console.WriteLine("Usage: twimgdump <user-screen-name> [<cursor>]");

                return;
            }

            using var client = new MediaTimelineClient();
            using var downloader = new MediaDownloader(userScreenName);

            var currentCursor = (string?)args.ElementAtOrDefault(1);
            IList<(string TweetId, IList<string> MediaUrls)> tweets;
            try
            {
                do
                {
                    Console.WriteLine("Current cursor: {0}", currentCursor ?? "null");

                    string cursorTop;
                    string cursorBottom;
                    (tweets, cursorTop, cursorBottom) = await client.FetchTweetsAsync(userScreenName, currentCursor);

                    Console.WriteLine("Fetched {0} tweet(s).", tweets.Count);
                    Console.WriteLine("Next cursor: {0}", cursorBottom);

                    var flattenedTweets = tweets
                        .SelectMany(x => x.MediaUrls.Select(y => (x.TweetId, MediaUrl: y)))
                        .ToList();

                    foreach (var (tweetId, mediaUrl) in flattenedTweets)
                    {
                        // Media are intentially downloaded in sequence (as opposed to in parallel) to keep request rates
                        // low and stay clear of possible rate limiting.

                        await downloader.DownloadAsync(tweetId, mediaUrl);
                    }

                    Console.WriteLine("Downloaded {0} file(s).", flattenedTweets.Count);

                    currentCursor = cursorBottom;
                }
                while (tweets.Any());
            }
            catch (Exception e)
            {
                Console.WriteLine("An unhandled exception was thrown.");
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);

                return;
            }

            Console.WriteLine("All media have been downloaded.");
        }
    }
}
