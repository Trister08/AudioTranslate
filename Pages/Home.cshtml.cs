using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.IO;

namespace Audio_Translate.Pages
{
    public class HomeModel : PageModel
    {
        private readonly ILogger<HomeModel> _logger;

        private readonly string assemblyApiKey = "59c606248b1f4c3a8c3e3b150a321f8b"; // AssemblyAI for transcription
        private readonly string translateApiKey = "a3b35cf4e8msha00ce3bc33d7f79p1bcdbajsndb87f71092e2"; // Deep Translate for translation
        private readonly string ttsApiKey = "147a661867mshfe4aa404dfc187fp11bcd4jsn38c749e45344"; // CloudLabs Text-to-Speech

        public HomeModel(ILogger<HomeModel> logger)
        {
            _logger = logger;
        }

        [BindProperty]
        public IFormFile UploadedFile { get; set; }

        [BindProperty]
        public string Language { get; set; } // 'af' for Afrikaans, 'zu' for Zulu

        [BindProperty]
        public string Voice { get; set; } // Voice codes like 'af-ZA-1', 'af-ZA-2', 'zu-ZA-1', 'zu-ZA-2'

        public string Transcription { get; set; }
        public string Translation { get; set; }
        public string AudioUrl { get; set; }

        public string Label3 { get; set; } = "Voice Translation Application";
        public string Label4 { get; set; } = "Translation Results";
        public string Label2 { get; set; } = "Enjoy your translated audio and text.";

        public async Task<IActionResult> OnPostAsync()
        {
            if (UploadedFile != null)
            {
                var uploadsFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");

                if (!Directory.Exists(uploadsFolderPath))
                {
                    Directory.CreateDirectory(uploadsFolderPath);
                }

                var filePath = Path.Combine(uploadsFolderPath, UploadedFile.FileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await UploadedFile.CopyToAsync(stream);
                }

                var audioUrl = await UploadAudio(filePath);
                if (!string.IsNullOrEmpty(audioUrl))
                {
                    Transcription = await GetTranscription(audioUrl);
                    if (!string.IsNullOrEmpty(Transcription))
                    {
                        Translation = await TranslateText(Transcription, Language);
                        AudioUrl = await ConvertTextToSpeech(Translation, Voice);

                        if (string.IsNullOrEmpty(AudioUrl))
                        {
                            ModelState.AddModelError(string.Empty, "Failed to convert text to speech.");
                        }
                    }
                    else
                    {
                        ModelState.AddModelError(string.Empty, "Failed to get transcription.");
                    }
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Failed to upload audio.");
                }
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Please select an audio file to upload.");
            }

            return Page();
        }

        private async Task<string> UploadAudio(string filePath)
        {
            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri("https://api.assemblyai.com/v2/upload"),
                    Headers =
                    {
                        { "authorization", assemblyApiKey }
                    },
                    Content = new ByteArrayContent(await System.IO.File.ReadAllBytesAsync(filePath))
                    {
                        Headers =
                        {
                            ContentType = new MediaTypeHeaderValue("application/octet-stream")
                        }
                    }
                };

                using (var response = await client.SendAsync(request))
                {
                    response.EnsureSuccessStatusCode();
                    var body = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(body);
                    return json["upload_url"]?.ToString();
                }
            }
        }

        private async Task<string> GetTranscription(string audioUrl)
        {
            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri("https://api.assemblyai.com/v2/transcript"),
                    Headers =
                    {
                        { "authorization", assemblyApiKey }
                    },
                    Content = new StringContent($"{{\"audio_url\":\"{audioUrl}\"}}")
                    {
                        Headers =
                        {
                            ContentType = new MediaTypeHeaderValue("application/json")
                        }
                    }
                };

                using (var response = await client.SendAsync(request))
                {
                    response.EnsureSuccessStatusCode();
                    var body = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(body);
                    var transcriptId = json["id"]?.ToString();

                    return await PollTranscriptionResult(transcriptId);
                }
            }
        }

        private async Task<string> PollTranscriptionResult(string transcriptId)
        {
            using (var client = new HttpClient())
            {
                var url = $"https://api.assemblyai.com/v2/transcript/{transcriptId}";
                while (true)
                {
                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Get,
                        RequestUri = new Uri(url),
                        Headers =
                        {
                            { "authorization", assemblyApiKey }
                        }
                    };

                    using (var response = await client.SendAsync(request))
                    {
                        response.EnsureSuccessStatusCode();
                        var body = await response.Content.ReadAsStringAsync();
                        var json = JObject.Parse(body);
                        var status = json["status"]?.ToString();

                        if (status == "completed")
                        {
                            return json["text"]?.ToString();
                        }
                        else if (status == "failed")
                        {
                            return "Transcription failed.";
                        }
                    }

                    await Task.Delay(5000);
                }
            }
        }

        private async Task<string> TranslateText(string text, string targetLanguage)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Post,
                        RequestUri = new Uri("https://deep-translate1.p.rapidapi.com/language/translate/v2"),
                        Headers =
                        {
                            { "x-rapidapi-key", translateApiKey },
                            { "x-rapidapi-host", "deep-translate1.p.rapidapi.com" }
                        },
                        Content = new StringContent($"{{\"q\":\"{text}\",\"source\":\"en\",\"target\":\"{targetLanguage}\"}}")
                        {
                            Headers =
                            {
                                ContentType = new MediaTypeHeaderValue("application/json")
                            }
                        }
                    };

                    using (var response = await client.SendAsync(request))
                    {
                        response.EnsureSuccessStatusCode();
                        var body = await response.Content.ReadAsStringAsync();
                        _logger.LogInformation("API Response: " + body); // Log the full response

                        var json = JObject.Parse(body);

                        if (json["data"]?["translations"]?["translatedText"] != null)
                        {
                            return json["data"]["translations"]["translatedText"]?.ToString();
                        }
                        else
                        {
                            return "Translation result is not available.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during translation.");
                return $"Error occurred: {ex.Message}";
            }
        }

        private async Task<string> ConvertTextToSpeech(string text, string voiceCode)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Post,
                        RequestUri = new Uri("https://cloudlabs-text-to-speech.p.rapidapi.com/synthesize"),
                        Headers =
                        {
                            { "x-rapidapi-key", ttsApiKey },
                            { "x-rapidapi-host", "cloudlabs-text-to-speech.p.rapidapi.com" }
                        },
                        Content = new MultipartFormDataContent
                        {
                            { new StringContent(voiceCode) { Headers = { ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "voice_code" } } } },
                            { new StringContent(text) { Headers = { ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "text" } } } },
                            { new StringContent("1.00") { Headers = { ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "speed" } } } },
                            { new StringContent("1.00") { Headers = { ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "pitch" } } } },
                            { new StringContent("audio_url") { Headers = { ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "output_type" } } } }
                        }
                    };

                    using (var response = await client.SendAsync(request))
                    {
                        response.EnsureSuccessStatusCode();
                        var responseBody = await response.Content.ReadAsStringAsync();
                        var json = JObject.Parse(responseBody);
                        return json["result"]["audio_url"]?.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during text-to-speech conversion.");
                return $"Error occurred: {ex.Message}";
            }
        }
    }
}





