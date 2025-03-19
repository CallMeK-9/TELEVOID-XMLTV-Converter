using System.ComponentModel;
using System.Xml;
using System.Xml.Linq;
using System.Text.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ImageMagick;
using CommandLine;

namespace XMLTV_Converter;

class Program
{

    public class Options
    {
        [Option("o", "output", Required = false, HelpText = "The path to output the guide JSON and JPG to. Defaults to ./guide", DefaultValue = "./guide")]
        public string OutPath { get; set; }

        [Option("i", "input", Required = true, HelpText = "The URL for the XMLTV guide information to convert. Required.")]
        public string XMLTVString { get; set; }

        [Option("l", "length", Required = false, HelpText = "The approximate maximum amount of hours of information to write for each channel. Defaults to 8.", DefaultValue = 8d)]
        public double HoursToConvert { get; set; }

        [Option("r", "replacements", Required = false, HelpText = "The path to the replacements JSON file. Defaults to ./replacements.json", DefaultValue = "./replacements.json")]
        public string ReplacementsJSONPath { get; set; }

        [Option("p", "posters", Required = false, HelpText = "The path to the replacements image folder. Defaults to ./replacementposters", DefaultValue = "./replacementposters")]
        public string ReplacementsPosterPath { get; set; }

        [Option("c", "cache", Required = false, HelpText = "The path to the poster image cache folder. Defaults to ./cachedposters", DefaultValue = "./cachedposters")]
        public string PosterCachePath { get; set; }
    }

    static void Main(string[] args)
    {
        Options options = new Options();
        CommandLineParser.Default.ParseArguments(args, options);

        string outPath = options.OutPath + "/guide.json";
        string imageOutPath = options.OutPath + "/guide.jpg";
        double HoursToConvert = options.HoursToConvert;
        string ReplacementsJSONPath = options.ReplacementsJSONPath;
        string ReplacementsPosterPath = options.ReplacementsPosterPath;
        string PosterCachePath = options.PosterCachePath;

        Directory.CreateDirectory(options.OutPath);
        Directory.CreateDirectory(ReplacementsPosterPath);
        Directory.CreateDirectory(PosterCachePath);

        WebClient webClient = new WebClient();
        XDocument XMLTV = XDocument.Load(options.XMLTVString);
        string[] cachedImages = Directory.GetFiles(PosterCachePath + "/");
        Dictionary<string, byte[]> knownImages = new Dictionary<string, byte[]>();
        List<Replacement> replacements = new List<Replacement>();
        List<Channel> availableChannels = new List<Channel>();

        FillReplacementsList();

        foreach (XElement XMLChannel in XMLTV.Descendants("channel"))
        {
            availableChannels.Add(new Channel(XMLChannel.Elements("display-name").First().Value));
        }

        foreach (XElement XMLProgramme in XMLTV.Descendants("programme"))
        {
            int channelID = int.Parse(XMLProgramme.Attribute("channel").Value.Split('.')[0]);

            XElement title = XMLProgramme.Elements("title").First(); // must always be present
            XAttribute startDate = XMLProgramme.Attribute("start"); // must always be present
            XAttribute endDate = XMLProgramme.Attribute("stop");

            if (DateTime.Compare(DateTime.ParseExact(endDate.Value, "yyyyMMddHHmmss zzz", null), DateTime.Now) >= 0 && DateTime.Compare(DateTime.ParseExact(startDate.Value, "yyyyMMddHHmmss zzz", null), DateTime.Now.AddHours(HoursToConvert)) < 0)
            {
                Episode episodeBuilder = new Episode(title.Value, DateTime.ParseExact(startDate.Value, "yyyyMMddHHmmss zzz", null));
                XElement episodeTitle = XMLProgramme.Elements("sub-title").FirstOrDefault();
                var locatedReplacements = replacements.Where(replacement => replacement.ReplacementName == title.Value);
                if (locatedReplacements.Any())
                {
                    Replacement locatedreplacement = locatedReplacements.FirstOrDefault();
                    episodeBuilder.EpisodePlot = locatedreplacement.ReplacementPlot;
                    episodeBuilder.Thumbnail = locatedreplacement.ReplacementImage;
                }
                else
                {
                    XElement icon = XMLProgramme.Elements("icon").FirstOrDefault();
                    if (icon != null)
                    {
                        episodeBuilder.PreviewURL = icon.Attribute("src").Value;
                        if (knownImages.TryGetValue(episodeBuilder.PreviewURL, out byte[] image))
                        {
                            episodeBuilder.Thumbnail = image;
                        }
                        else
                        {
                            try
                            {
                                string urlHash;
                                using (SHA256 sha256hash = SHA256.Create())
                                {
                                    urlHash = GetHash(sha256hash, episodeBuilder.PreviewURL);
                                }

                                if (cachedImages.Contains(PosterCachePath + "/" + urlHash + ".jpg"))
                                {
                                    episodeBuilder.Thumbnail = File.ReadAllBytes(PosterCachePath + "/" + urlHash + ".jpg");
                                }
                                else
                                {
                                    episodeBuilder.Thumbnail = webClient.DownloadData(episodeBuilder.PreviewURL);
                                    File.WriteAllBytes(PosterCachePath + "/" + urlHash + ".jpg", episodeBuilder.Thumbnail);
                                    cachedImages = Directory.GetFiles(PosterCachePath + "/");
                                }
                                knownImages.Add(episodeBuilder.PreviewURL, episodeBuilder.Thumbnail);
                            }
                            catch (WebException e)
                            {
                                Console.WriteLine(e);
                            }
                        }
                    }

                    XElement plot = XMLProgramme.Elements("desc").FirstOrDefault();
                    if (plot != null)
                    {
                        episodeBuilder.EpisodePlot = plot.Value;
                    }
                }

                if (episodeTitle != null)
                {
                    episodeBuilder.Title = episodeTitle.Value;
                }

                XElement number = XMLProgramme.Elements("episode-num").FirstOrDefault();
                if (number != null)
                {
                    episodeBuilder.EpisodeNumber = number.Value;
                }

                availableChannels.Where(Channel => Channel.ChannelId == channelID).First().AddEpisode(episodeBuilder);
            }
        }

        int availableImageSlots = 64;
        int usedImageSlots = 0;
        bool canBakeMore = true;
        Dictionary<string, int> bakedImages = new Dictionary<string, int>();

        using (MagickImage canvas = new MagickImage(MagickColors.White, 2048, 2048))
        {
            foreach (var availableChannel in availableChannels)
            {
                foreach (var episode in availableChannel.Episodes)
                {
                    if (episode.HasPreview())
                    {
                        if (bakedImages.ContainsKey(episode.ShowTitle)) // we already baked this one, so just return the known index
                        {
                            bakedImages.TryGetValue(episode.ShowTitle, out int knownIndex);
                            episode.PreviewIndex = knownIndex;
                        }
                        else
                        {
                            if (usedImageSlots+1>availableImageSlots) { canBakeMore = false; }
                            if (canBakeMore)
                            {
                                using (MagickImage image = new MagickImage(episode.Thumbnail))
                                {
                                    image.Resize(new MagickGeometry(256, 256) { IgnoreAspectRatio = true });

                                    // Calculate the position to place the image in the mosaic
                                    int row = usedImageSlots / 8;
                                    int col = usedImageSlots % 8;
                                    int posX = col * 256;
                                    int posY = row * 256;

                                    canvas.Composite(image, posX, posY);
                                    usedImageSlots++;
                                    bakedImages.Add(episode.ShowTitle, usedImageSlots);
                                    episode.PreviewIndex = usedImageSlots;
                                }
                            }
                        }  
                    }
                }
            }
            canvas.Write(imageOutPath);
        }

        using (var stream = new System.IO.MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartArray();
                foreach (var availableChannel in availableChannels)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("name");
                    writer.WriteStringValue(availableChannel.Name);
                    writer.WritePropertyName("media");
                    writer.WriteStartArray();
                    foreach (var episode in availableChannel.Episodes)
                    {
                        if (DateTime.Compare(episode.StartDate, DateTime.Now.AddHours(HoursToConvert)) > 0) break; // messy
                        writer.WriteStartObject();
                        writer.WritePropertyName("name");
                        writer.WriteStringValue(episode.ShowTitle);
                        writer.WritePropertyName("startDate");
                        writer.WriteStringValue(episode.StartDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
                        writer.WritePropertyName("info");
                        writer.WriteStartObject();
                        if (episode.Title != null)
                        {
                            writer.WritePropertyName("episode");
                            writer.WriteStringValue(episode.Title);
                        }
                        if (episode.EpisodePlot != null)
                        {
                            writer.WritePropertyName("plot");
                            writer.WriteStringValue(episode.EpisodePlot);
                        }
                        if (episode.PreviewURL != null)
                        {
                            writer.WritePropertyName("image");
                            writer.WriteNumberValue(episode.PreviewIndex);
                        }
                        writer.WriteEndObject();
                        writer.WritePropertyName("episodeNumber");
                        writer.WriteStringValue(episode.EpisodeNumber);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            // Convert the JSON document to string
            string jsonString = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            File.WriteAllText(outPath, jsonString);
        }

        void FillReplacementsList()
        {
            if (File.Exists(ReplacementsJSONPath))
            {
                string replacementsfile = File.ReadAllText(ReplacementsJSONPath);
                List<JSONReplacement> JSONReplacements = JsonSerializer.Deserialize<List<JSONReplacement>>(replacementsfile);

                foreach (var jsonreplacement in JSONReplacements)
                {
                    replacements.Add(new Replacement(jsonreplacement.name, jsonreplacement.description, ReplacementsPosterPath + "/" + jsonreplacement.poster));
                }
            }
        }
    }

    private static string GetHash(HashAlgorithm hashAlgorithm, string input)
    {
        byte[] data = hashAlgorithm.ComputeHash(Encoding.UTF8.GetBytes(input));
        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < data.Length; i++)
        {
            sb.Append(data[i].ToString("x2"));
        }

        return sb.ToString();
    }

    private static bool VerifyHash(HashAlgorithm hashAlgorithm, string input, string hash)
    {
        string? hashOfInput = GetHash(hashAlgorithm, input);
        StringComparer comparer = StringComparer.OrdinalIgnoreCase;

        return comparer.Compare(hashOfInput, hash) == 0;
    }
}

public class Channel
{
    private int _channelId;
    public int ChannelId
    {
        get { return _channelId; }
        set { _channelId = value; }
    }
    private string _channelName;
    public string Name
    {
        get { return _channelName; }
        set { _channelName = value; }
    }
    private List<Episode> _episodes;
    public List<Episode> Episodes
    {
        get { return _episodes.OrderBy(e => e.StartDate).ToList(); }
    }
    public Channel(string channelIdent)
    {
        string[] identSplit = channelIdent.Split(' ');
        _channelName = identSplit[1];
        _channelId = int.Parse(identSplit[0]);
        _episodes = new List<Episode>();
    }

    public void AddEpisode(Episode episode)
    {
        _episodes.Add(episode);
    }

}

public class Episode
{
    private string _showTitle;
    public string ShowTitle
    {
        get { return _showTitle; }
        set { _showTitle = value; }
    }
    private DateTime _startDate;
    public DateTime StartDate
    {
        get { return _startDate; }
        set { _startDate = value; }
    }

    private bool _hasNumber = false;
    private string _number;
    public string EpisodeNumber
    {
        get { return _number; }
        set { _hasNumber = true; _number = value; }
    }

    private bool _hasTitle = false;
    private string _title;
    public string Title
    {
        get { return _title; }
        set { _hasTitle = true; _title = value; }
    }

    private bool _hasPlot = false;
    private string _episodePlot;
    public string EpisodePlot
    {
        get { return _episodePlot; }
        set { _hasPlot = true; _episodePlot = value; }
    }

    private bool _hasPreview = false;
    private int _previewIndex;
    public int PreviewIndex
    {
        get { return _previewIndex; }
        set { _hasPreview = true; _previewIndex = value; }
    }

    private string _previewURL;
    public string PreviewURL
    {
        get { return _previewURL; }
        set { _previewURL = value; }
    }

    private byte[] _thumbnail;
    public byte[] Thumbnail
    {
        get { return _thumbnail; }
        set { _hasPreview = true; _thumbnail = value; }
    }

    // an episode must be initialized with, at minimum, a show name and start date.
    public Episode(string _constructorShowTitle, DateTime _constructorStartDate)
    {
        _showTitle = _constructorShowTitle;
        _startDate = _constructorStartDate;

        _number = "";
        _title = "";
        _episodePlot = "";
        _previewIndex = 0;
        _previewURL = "";
    }

    public bool HasPreview()
    {
        return _hasPreview;
    }

    public override string ToString()
    {
        return String.Format(
            "Show: {0}\nEpisode: {1} {2}\nStart Date: {3}\nPreview URL: {4}\nPlot: {5}",
            _showTitle,
            _number,
            _title,
            _startDate.ToString("h:mm tt"),
            _previewURL,
            _episodePlot
        );
    }
}

public class Replacement()
{
    private string _replacementName;
    private string _replacementPlot;
    private byte[] _replacementImage;

    public string ReplacementName
    {
        get { return _replacementName; }
        set { _replacementName = value; }
    }

    public string ReplacementPlot
    {
        get { return _replacementPlot; }
        set { _replacementPlot = value; }
    }

    public byte[] ReplacementImage
    {
        get { return _replacementImage; }
        set { _replacementImage = value; }
    }

    public Replacement(string replacementName, string replacementPlot, string replacementImage) : this()
    {
        this._replacementName = replacementName;
        this._replacementPlot = replacementPlot;
        this._replacementImage = File.ReadAllBytes(replacementImage);
    }
}

public class JSONReplacement
{
    public string name { get; set; }
    public string description { get; set; }
    public string poster { get; set; }
}