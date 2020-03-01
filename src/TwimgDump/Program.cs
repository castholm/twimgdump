using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TwimgDump.CommandLine;

namespace TwimgDump
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            IList<string> positionalArgs;
            IDictionary<string, IList<string>> opts;
            try
            {
                (positionalArgs, opts) = CommandLineArgsParser.Parse(args, new (string?, string, bool)[]
                {
                    ("c", "cursor", true),
                    ("h", "help", false),
                    ("o", "output", true),
                    ("V", "version", false),
                });
            }
            catch (InvalidOperationException exception)
            {
                Console.WriteLine($"Error: {exception.Message}");
                PrintUsage();

                return;
            }

            if (opts.TryGetValue("help", out _))
            {
                Console.WriteLine(string.Join(
                    Environment.NewLine,
                    "Usage:",
                    "  twimgdump [options] [--] <user-screen-name>",
                    "",
                    "Options:",
                    "  -c, --cursor <cursor>            Set the initial cursor value.",
                    "  -h, --help                       Display this help text.",
                    "  -o, --output <output-directory>  Set the output directory.",
                    "  -V, --version                    Display the version number."));

                return;
            }

            if (opts.TryGetValue("version", out _))
            {
                var version = typeof(Program)
                    .Assembly
                    .GetCustomAttribute<AssemblyFileVersionAttribute>()
                    ?.Version
                    ?? "Unknown version.";

                Console.WriteLine(version);

                return;
            }

            var userScreenName = (string?)positionalArgs.ElementAtOrDefault(0);
            if (userScreenName is null)
            {
                PrintUsage();

                return;
            }

            if (positionalArgs.Count > 1)
            {
                Console.WriteLine($"Error: Unknown positional argument '{positionalArgs.ElementAt(1)}'.");
                PrintUsage();

                return;
            }

            var outputDirectory = opts.TryGetValue("output", out var outputDirectoryArgs)
                ? outputDirectoryArgs.First()
                : userScreenName;

            var currentCursor = opts.TryGetValue("cursor", out var cursorArgs)
                ? cursorArgs.First()
                : null;

            using var client = new MediaTimelineClient();
            using var downloader = new MediaDownloader(outputDirectory);

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
                        .SelectMany(x => x.MediaUrls.Select((mediaUrl, i) =>
                        {
                            string extension;
                            var match = Regex.Match(mediaUrl, @"\?format=([^#&]*)");
                            if (match.Success)
                            {
                                extension = $".{match.Groups[1].Value}";
                            }
                            else
                            {
                                extension = Path.GetExtension(new Uri(mediaUrl).Segments.Last());
                            }

                            var filename = $"{x.TweetId}{(x.MediaUrls.Count > 1 ? $"-{i + 1}" : "")}{extension}";

                            return (MediaUrl: mediaUrl, Filename: filename);
                        }))
                        .ToList();

                    foreach (var (mediaUrl, filename) in flattenedTweets)
                    {
                        // Media is intentially downloaded in sequence (as opposed to in parallel) to keep request
                        // rates low and stay clear of potential rate limiting.

                        await downloader.DownloadAsync(mediaUrl, filename);
                    }

                    Console.WriteLine("Downloaded {0} file(s).", flattenedTweets.Count);

                    currentCursor = cursorBottom;
                }
                while (tweets.Any());
            }
            catch (Exception e)
            {
                Console.WriteLine("An unhandled exception was thrown by the application.");
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);

                return;
            }

            Console.WriteLine("All media has been downloaded.");
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: twimgdump [options] [--] <user-screen-name>");
            Console.WriteLine("Try 'twimgdump --help' for more information.");
        }
    }
}
