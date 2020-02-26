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
            var userScreenName = args.ElementAtOrDefault(0);
            if (userScreenName is null)
            {
                Console.WriteLine("Usage: twimg-dump <user-screen-name>");

                return;
            }

            using var client = new MediaTimelineClient();

            string? currentCursor = null;
            IList<(string TweetId, IList<string> MediaUrls)> tweets;
            do
            {
                string cursorTop;
                string cursorBottom;
                (tweets, cursorTop, cursorBottom) = await client.FetchTweetsAsync(userScreenName, currentCursor);

                // TODO: Actually download the media.

                currentCursor = cursorBottom;
            }
            while (tweets.Any());
        }
    }
}
