using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StrEnum = System.Collections.Generic.IEnumerable<string>;

// Simple spell checker console application developed in C#
// Based on Peter Norvig's write up in Python: http://norvig.com/spell-correct.html 
static class SpellChecker
{

    // Initialize the character alphabet to all 26 lower case members of the alphabet
    private readonly static string ALPHABET = new string(Enumerable.Range('a', 26).Select(x => (char)x).ToArray());

    // The URL used to initialize the dictionary file used for our spell checker
    const string DICTIONARY_FILE_URL = "http://norvig.com/big.txt";

    // The local file name used to store the dictionary
    const string LOCAL_DICTIONARY_FILE_NAME = "big.txt";

    // The user agent and header strings utilized in downloading dictionary file from the URL
    const string USER_AGENT_STRING = "User-Agent: Mozilla/5.0 (compatible; MSIE 10.6; Windows NT 6.1; Trident/5.0; InfoPath.2; SLCC1; .NET CLR 3.0.4506.2152; .NET CLR 3.5.30729; .NET CLR 2.0.50727) 3gpp-gba UNTRUSTED/1.0";
    const string ACCEPT_HEADER_STRING = "Accept: text/html, application/xhtml+xml, */*";

    // Return true if the passed in collection is null or empty, and false otherwise
    static bool HasElements<T>(IEnumerable<T> collection)
    {
        return collection != null && collection.Any();
    }

    // Return whether a non-empty file exists located at input path <filePath>
    static bool FileExists(string filePath)
    {
        return File.Exists(filePath) && new FileInfo(filePath).Length >= 0;
    }

    // Download the file at url <url> and save it to the <savePath> location.
    static void DownloadFile(string url, string savePath)
    {
        try
        {

            Console.Write("Downloading dictionary file...");

            Uri address = new Uri(url);

            using (WebClient client = new WebClient())
            {

                // Add an Accept header so the server knows what the client wants/accepts.
                client.Headers.Add(ACCEPT_HEADER_STRING);
                // Add user agent header since some websites do not allow clients with no user-agent specified.
                client.Headers.Add(USER_AGENT_STRING);

                client.DownloadFile(address, savePath);

            }
        }
        catch (WebException e)
        {
            HandleException(e);
        }
    }

    // Fetch a list of words by reading from the Console
    static StrEnum FetchWordList()
    {
        Console.Write("Input a list of words: ");
        var words = Console.ReadLine().ToLower().Split();
        return words;
    }

    // Get a list of possible corrections/edits for the string word with an edit distance of one
    static StrEnum GetEdits(string word)
    {

        // Get list of corrections/edits by evaluating all letter removal permutations
        var deleteEdits = (from i in Enumerable.Range(0, word.Length)
                           select word.Substring(0, i) + word.Substring(i + 1));

        // Get list of corrections/edits by evaluating all letter swapping permutations
        var transposeEdits = (from i in Enumerable.Range(0, word.Length)
                              select word.Substring(0, i) + word.Substring(i + 1));

        // Get list of corrections/edits by evaluating all letter replacement permutations
        var replaceEdits = (from i in Enumerable.Range(0, word.Length)
                            from c in ALPHABET
                            select word.Substring(0, i) + c + word.Substring(i + 1));

        // Get list of corrections/edits by evaluating all  letter insertion permutations
        var insertEdits = (from i in Enumerable.Range(0, word.Length + 1)
                           from c in ALPHABET
                           select word.Substring(0, i) + c + word.Substring(i));

        // Return a collection of all of the correctons/edits 
        return deleteEdits.Union(transposeEdits).Union(replaceEdits).Union(insertEdits);

    }

    // Fetch a map of corrections for each string in the input <words> which maps each word to its lowest edit
    // distance / highest word count string, and uses the passed in <wordCountMap> to evaluate the total word
    // count for the matches. Also, note that the maximum edit distance we use is two.
    static IDictionary<string, string> FetchCorrectionMap(StrEnum words, IDictionary<string, int> wordCountMap)
    {
        // Declare a concurrent map to store our word correction mapping suggestions (map: original word => correction/edit word)
        IDictionary<string, string> correctionMap = new ConcurrentDictionary<string, string>();

        // Process each individual word in parallel
        Parallel.ForEach(words, (Action<string>)((word) =>
        {
            // Fetch a list of edits/corrections for our word by iterating through all possible 
            // corrections with an edit distance of one or two characters 
            var distanceOneEdits = (from edit in GetEdits(word) where wordCountMap.ContainsKey(word) select edit).Distinct();

            var distanceTwoEdits = (from oneEdits in GetEdits(word)
                                    from twoEdits in GetEdits(oneEdits)
                                    where wordCountMap.ContainsKey(twoEdits)
                                    select twoEdits).Distinct();

            // If our dictionary already contains the word, simply return an array with the word in it
            // Otherwise, use the collection with the lowest edit distance 
            var corrections = (wordCountMap.ContainsKey(word) ? new[] { word }
                              : HasElements(distanceOneEdits) ? distanceOneEdits
                              : HasElements(distanceTwoEdits) ? distanceTwoEdits
                              : Enumerable.Empty<string>());

            // Set the result mapping to contain the correction with the lowest edit distance
            // and highest word count value fetched from processing our dictionary file
            correctionMap[word] = !HasElements(corrections) ? word : (from correction in corrections
                                                                      where wordCountMap.ContainsKey(correction)
                                                                      orderby wordCountMap[correction] descending
                                                                      select correction).First();
        }));

        return correctionMap;

    }

    // Handle input exception <e> by writing its string representation to the Console.
    static void HandleException(Exception e)
    {
        Console.WriteLine(e.ToString());
    }

    static void Main()
    {
        // Initialize the file name and path of our local dictionary file 
        string DICTIONARY_FILE_PATH = System.IO.Directory.GetCurrentDirectory() + @"\" + LOCAL_DICTIONARY_FILE_NAME;

        // If the local dictionary file does not exist, download it from the url
        if (!FileExists(DICTIONARY_FILE_PATH))
        {
            DownloadFile(DICTIONARY_FILE_URL, DICTIONARY_FILE_PATH);
        }

        // Extract a list of individual words from our local dictionary file and map them to the 
        // number of times they have been seen (map: word => word count)
        var wordCountMap = (from Match word in Regex.Matches(File.ReadAllText(DICTIONARY_FILE_PATH).ToLower(), "[a-z]+")
                            group word.Value by word.Value)
                        .ToDictionary(group => group.Key, group => group.Count());

        // Continue looping and fetching user input until the user enters a blank line or 'exit'
        while (true)
        {

            // Fetch a list of words we will perform our corrections on
            StrEnum words = FetchWordList();

            // Check if the user has entered a blank line, or if he/she has typed in 'exit', in which case we 
            // stop looping and exit the console application
            if (
                words.Count() == 1 
                && (
                       words.First().Equals("") 
                    || words.First().Equals("exit", StringComparison.OrdinalIgnoreCase)
                    )
                )
            {
                return;
            }

            // Fetch a spelling correction mapping for the word input using the edit distance and input word count 
            // map to select the correction for each word
            var correctionMap = FetchCorrectionMap(words, wordCountMap);

            // Replace the words in our list with the list of suggested corrections
            var replacements = (from word in words
                                select correctionMap[word]).ToArray();

            // Write the resulting list of corrections to the console
            Console.WriteLine(string.Join(" ", replacements));

        }

    }
}
