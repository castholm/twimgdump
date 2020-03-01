using System;
using System.Collections.Generic;
using System.Linq;

namespace TwimgDump.CommandLine
{
    internal static class CommandLineArgsParser
    {
        public static (IList<string> PositionalArgs, IDictionary<string, IList<string>> Opts) Parse(
            IEnumerable<string> args,
            IEnumerable<(string? Short, string Long, bool TakesArg)>? mappings = null)
        {
            var shortToLongMappings = mappings
                ?.Where(x => x.Short is object)
                .ToDictionary(x => Convert.ToChar(x.Short!), x => x.Long)
                ?? new Dictionary<char, string>();

            var longToTakesArgMappings = mappings
                ?.ToDictionary(x => x.Long, x => x.TakesArg)
                ?? new Dictionary<string, bool>();

            var positionalArgs = new List<string>();
            var opts = new Dictionary<string, IList<string>>();

            using var enumerator = args.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var currentArg = enumerator.Current;

                if (currentArg == "--")
                {
                    while (enumerator.MoveNext())
                    {
                        positionalArgs.Add(enumerator.Current);
                    }

                    break;
                }

                if (currentArg.StartsWith("--"))
                {
                    var longNameAndMaybeValue = currentArg.Substring(2).Split("=", 2);
                    var longName = longNameAndMaybeValue.ElementAt(0);
                    var value = (string?)longNameAndMaybeValue.ElementAtOrDefault(1);

                    if (!longToTakesArgMappings.TryGetValue(longName, out var takesArg))
                    {
                        throw new InvalidOperationException($"Unknown option '--{longName}'.");
                    }

                    if (!takesArg)
                    {
                        if (value is object)
                        {
                            throw new InvalidOperationException($"Option '--{longName}' takes no argument.");
                        }

                        AddOpt(longName, value: null);

                        continue;
                    }

                    if (value is object)
                    {
                        AddOpt(longName, value);

                        continue;
                    }

                    if (!enumerator.MoveNext())
                    {
                        throw new InvalidOperationException($"Option '--{longName}' requires an argument.");
                    }

                    AddOpt(longName, enumerator.Current);

                    continue;
                }

                if (currentArg == "-")
                {
                    positionalArgs.Add(currentArg);

                    continue;
                }

                if (currentArg.StartsWith("-"))
                {
                    for (int i = 1; i < currentArg.Length; i++)
                    {
                        var shortName = currentArg[i];
                        if (!shortToLongMappings.TryGetValue(shortName, out var longName))
                        {
                            throw new InvalidOperationException($"Unknown option '-{shortName}'.");
                        }

                        var takesArg = longToTakesArgMappings[longName];
                        if (!takesArg)
                        {
                            AddOpt(longName, value: null);

                            continue;
                        }

                        i++;

                        if (i < currentArg.Length)
                        {
                            AddOpt(longName, currentArg.Substring(i));

                            break;
                        }

                        if (!enumerator.MoveNext())
                        {
                            throw new InvalidOperationException($"Option '-{shortName}' requires an argument.");
                        }

                        AddOpt(longName, enumerator.Current);

                        break;
                    }

                    continue;
                }

                positionalArgs.Add(currentArg);
            }

            return (positionalArgs, opts);

            void AddOpt(string name, string? value)
            {
                if (!opts.TryGetValue(name, out var values))
                {
                    values = new List<string>();
                    opts.Add(name, values);
                }

                if (value is object)
                {
                    values.Add(value);
                }
            }
        }
    }
}
