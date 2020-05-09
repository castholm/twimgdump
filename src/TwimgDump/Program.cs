using System;
using System.Collections.Generic;
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
                    "    twimgdump [options] [--] <username>",
                    "",
                    "Options:",
                    "    -c, --cursor <cursor>",
                    "        Sets the initial cursor value.",
                    "",
                    "    -h, --help",
                    "        Displays this help text.",
                    "",
                    "    -o, --output <output-file-path-template>",
                    "        Sets the template used to determine the output file paths of",
                    "        downloaded media.  The tokens '[user-id]', '[username]',",
                    "        '[tweet-id]', '[year]', '[month]', '[day]', '[hour]',",
                    "        '[minute]', '[second]', '[millisecond]', '[media-id]', '[stem]',",
                    "        '[extension]', '[index]', '[count]', '[width]' and '[height]'",
                    "        will be substituted by the corresponding attributes of retrieved",
                    "        media.",
                    "",
                    "    -V, --version",
                    "        Displays the version number."));

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

            var username = (string?)positionalArgs.ElementAtOrDefault(0);
            if (username is null)
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

            var outputPathTemplate = opts.TryGetValue("output", out var outputPathTemplateArgs)
                ? outputPathTemplateArgs.Last()
                : "[username]/[tweet-id]-[index][extension]";

            var currentCursor = opts.TryGetValue("cursor", out var cursorArgs)
                ? cursorArgs.Last()
                : null;

            using var client = new MediaTimelineClient(username);
            using var downloader = new MediaDownloader();

            IList<TweetMedia> mediaList;
            try
            {
                do
                {
                    Console.WriteLine("Current cursor: {0}", currentCursor ?? "null");

                    string cursorTop;
                    string cursorBottom;
                    (mediaList, cursorTop, cursorBottom) = await client.FetchTweetsAsync(currentCursor);

                    var tweetCount = mediaList.Select(x => x.TweetId).Distinct().Count();

                    Console.WriteLine("Fetched {0} tweet(s).", tweetCount);
                    Console.WriteLine("Next cursor: {0}", cursorBottom);

                    foreach (var media in mediaList)
                    {
                        // Media is intentially downloaded in sequence (as opposed to in parallel) to keep request
                        // rates low and stay clear of potential rate limiting.

                        var file = outputPathTemplate
                            .Replace("[user-id]", media.UserId.ToString(), StringComparison.OrdinalIgnoreCase)
                            .Replace("[username]", SanitizeFilenameComponent(media.Username), StringComparison.OrdinalIgnoreCase)
                            .Replace("[tweet-id]", media.TweetId.ToString(), StringComparison.OrdinalIgnoreCase)
                            .Replace("[year]", ((uint)media.Created.Year).ToString("D4"), StringComparison.OrdinalIgnoreCase)
                            .Replace("[month]", ((uint)media.Created.Month).ToString("D2"), StringComparison.OrdinalIgnoreCase)
                            .Replace("[day]", ((uint)media.Created.Day).ToString("D2"), StringComparison.OrdinalIgnoreCase)
                            .Replace("[hour]", ((uint)media.Created.Hour).ToString("D2"), StringComparison.OrdinalIgnoreCase)
                            .Replace("[minute]", ((uint)media.Created.Minute).ToString("D2"), StringComparison.OrdinalIgnoreCase)
                            .Replace("[second]", ((uint)media.Created.Second).ToString("D2"), StringComparison.OrdinalIgnoreCase)
                            .Replace("[millisecond]", ((uint)media.Created.Millisecond).ToString("D3"), StringComparison.OrdinalIgnoreCase)
                            .Replace("[media-id]", media.MediaId.ToString(), StringComparison.OrdinalIgnoreCase)
                            .Replace("[stem]", SanitizeFilenameComponent(media.Stem), StringComparison.OrdinalIgnoreCase)
                            .Replace("[extension]", SanitizeFilenameComponent(media.Extension), StringComparison.OrdinalIgnoreCase)
                            .Replace("[index]", ((uint)media.Index).ToString(), StringComparison.OrdinalIgnoreCase)
                            .Replace("[count]", ((uint)media.Count).ToString(), StringComparison.OrdinalIgnoreCase)
                            .Replace("[width]", ((uint)media.Width).ToString(), StringComparison.OrdinalIgnoreCase)
                            .Replace("[height]", ((uint)media.Height).ToString(), StringComparison.OrdinalIgnoreCase);

                        await downloader.DownloadAsync(media.Url, file);
                    }

                    Console.WriteLine("Downloaded {0} file(s).", mediaList.Count);

                    currentCursor = cursorBottom;
                }
                while (mediaList.Any());
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

        private static string SanitizeFilenameComponent(string input)
            => Regex.Replace(input, @"[^A-Za-z0-9\-_.]", "");
    }
}
