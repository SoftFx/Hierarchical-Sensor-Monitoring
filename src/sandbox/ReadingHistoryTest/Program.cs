using System.IO.Compression;
using System.Text;
using System.Text.Json;

const string Address = "https://localhost";
const int Port = 44330;

const string Key = "1aba29cf-eee8-4376-a72f-5fb01af0f7ff";
const string Path = "/datacollector/file";

HttpClientHandler handler = new()
{
    ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
};
var client = new HttpClient(handler, false);

var historyRequest = new
{
    Key,
    Path,
    From = DateTime.UtcNow.AddHours(-1),
    To = (DateTime?)null,
    Count = (int?)20,
    FileName = "test",
    Extension = ".csv",
    IsZipArchive = false,
};

try
{
    string jsonString = JsonSerializer.Serialize(historyRequest);
    var data = new StringContent(jsonString, Encoding.UTF8, "application/json");

    var response = await client.PostAsync($"{Address}:{Port}/api/sensors/historyFile", data);
    var content = await response.Content.ReadAsByteArrayAsync();

    if (historyRequest.IsZipArchive)
    {
        using var stream = new MemoryStream(content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var readmeEntry = archive.GetEntry($"{historyRequest.FileName}.{historyRequest.Extension}");

        using var reader = readmeEntry?.Open();
        using var streamReader = new StreamReader(reader);
        var originalContent = streamReader.ReadToEnd();

        Console.WriteLine(originalContent);
    }
    else
    {
        Console.WriteLine(Encoding.UTF8.GetString(content));

        // and if you want to have clear file values
        //var a = Encoding.UTF8.GetString(content);
        //var rows = a.Split('\n');

        //for (int i = 1; i < rows.Length - 1; i++)
        //{
        //    var columns = rows[i].Split(',');
        //    var val = columns[1];

        //    Console.WriteLine($"{val}     {Encoding.UTF8.GetString(Convert.FromBase64String(val))}");
        //}
    }
}
catch (Exception ex)
{
    Console.WriteLine(ex);
}

client.Dispose();