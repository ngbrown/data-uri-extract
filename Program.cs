using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace data_uri_extract
{
    class Program
    {
        private static async Task<int> Main(string[] args)
        {
            var fileInfo = new FileInfo(args[0]);
            if (!fileInfo.Exists)
            {
                Console.Error.WriteLine("File does not exist");
                return 1;
            }

            var outFileName = Path.Combine(fileInfo.DirectoryName,
                Path.GetFileNameWithoutExtension(fileInfo.Name) + "-new" + fileInfo.Extension);

            var resourceDirectory = fileInfo.DirectoryName;

            string input;
            using (var readStream = fileInfo.OpenText())
            {
                input = await readStream.ReadToEndAsync();
            }

            using (var writeStream = new StreamWriter(outFileName, false, Encoding.UTF8))
            {
                var regex = new Regex(@"(?<exp>url\(|(?<src>src|href)\s*=)\s*(?<quote>""|')?data:(?<mime>[^ \(\)<>@,;:\\""/\[\]\?=]+/[^ \(\)<>@,;:\\""/\[\]\?=]+);base64,",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled);
                var regexEndSpace = new Regex(@"\s|>", RegexOptions.Compiled);

                int x = 0;
                for (;;)
                {
                    var match = regex.Match(input, x);

                    if (!match.Success)
                    {
                        // done
                        break;
                    }

                    int base64startIndex = match.Index + match.Length;
                    int base64endIndex = base64startIndex;

                    await writeStream.WriteAsync(input.AsMemory(x, match.Index - x));
                    x = base64endIndex;

                    var mimeType = match.Groups["mime"].Value;
                    var matchExpression = match.Groups["exp"].Value;
                    var matchGroupQuote = match.Groups["quote"];
                    var matchHasQuote = matchGroupQuote.Success && matchGroupQuote.Length > 0;
                    if (matchExpression.StartsWith("url", StringComparison.OrdinalIgnoreCase))
                    {

                        int end;
                        if (matchHasQuote)
                        {
                            //match a quote - doesn't handle escaped version
                            end = input.IndexOf(matchGroupQuote.Value, base64startIndex, StringComparison.Ordinal);
                        }
                        else
                        {
                            end = input.IndexOf(')', base64startIndex);
                        }

                        if (end == -1)
                        {
                            Console.Error.WriteLine($"No end to capture starting at position: {base64startIndex}");
                            return 1;
                        }

                        base64endIndex = end;
                        var rawData = System.Convert.FromBase64String(input[base64startIndex..base64endIndex]);

                        var dataFileName = WriteFile(rawData, mimeType, resourceDirectory);
                        await writeStream.WriteAsync($"url({dataFileName})");

                        if (matchHasQuote)
                        {
                            var closeUrlIndex = input.IndexOf(')', base64endIndex + 1);
                            if (closeUrlIndex == -1)
                            {
                                Console.Error.WriteLine($"No end to url( starting at position: {base64endIndex}");
                                return 1;
                            }
                            x = closeUrlIndex + 1;
                        }
                        else
                        {
                            x = base64endIndex + 1;
                        }
                    }
                    else if (matchExpression.StartsWith("src", StringComparison.OrdinalIgnoreCase) || matchExpression.StartsWith("href", StringComparison.OrdinalIgnoreCase))
                    {
                        int end;
                        if (matchHasQuote)
                        {
                            //match a quote
                            end = input.IndexOf(matchGroupQuote.Value, base64startIndex, StringComparison.Ordinal);
                        }
                        else
                        {
                            //match a space
                            var endMatch = regexEndSpace.Match(input, base64startIndex);
                            end = endMatch.Success ? endMatch.Index : -1;
                        }

                        if (end == -1)
                        {
                            Console.Error.WriteLine($"No end to capture starting at position: {base64startIndex}");
                            return 1;
                        }

                        base64endIndex = end;
                        var rawData = System.Convert.FromBase64String(input[base64startIndex..base64endIndex]);

                        var dataFileName = WriteFile(rawData, mimeType, resourceDirectory);
                        await writeStream.WriteAsync($"{match.Groups["src"]}=\"{dataFileName}\"");

                        x = base64endIndex + (matchHasQuote ? 1 : 0);
                    }
                    else
                    {
                        Console.Error.WriteLine("Unknown match");
                        return 1;
                    }

                }

                await writeStream.WriteAsync(input.AsMemory(x));
            }

            return 0;
        }

        static ContentTypeMapping contentTypeMapping = new ContentTypeMapping();

        private static string WriteFile(byte[] rawData, string mimeType, string resourceDirectory)
        {
            string extension;
            if (!contentTypeMapping.TryGetExtension(mimeType, out extension))
            {
                Console.Error.WriteLine($"Unknown mime type: '{mimeType}'.");
                extension = ".bin";
            }

            var hash = Hash(rawData);
            var imgName = "File_" + hash.Substring(0, 8) + extension;
            var imagePath = Path.Combine(resourceDirectory, imgName);
            File.WriteAllBytes(imagePath, rawData);

            return imgName;
        }

        static string Hash(byte[] buffer)
        {
            using (var sha1 = new SHA1Managed())
            {
                var hash = sha1.ComputeHash(buffer);
                var sb = new StringBuilder(hash.Length * 2);

                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }
    }
}

