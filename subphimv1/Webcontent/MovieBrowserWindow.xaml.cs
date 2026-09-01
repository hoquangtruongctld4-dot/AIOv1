using subphimv1.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace subphimv1
{
    public class MovieSummary
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string PosterUrl { get; set; }
        public string EpisodeInfo { get; set; } 
    }
    public class HomePageData
    {
        public MovieSummary[] NewlyUpdated { get; set; }
        // public MovieSummary[] Featured { get; set; }
        // public RankingItem[] Rankings { get; set; }
    }
    public class Episode
    {
        public string Id { get; set; }
        public string Label { get; set; } 
    }
    public class MovieDetails
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string PosterUrl { get; set; }
        public string Status { get; set; }
        public string Genre { get; set; }
        public int Year { get; set; }
        public string Synopsis { get; set; }
        public Episode[] Episodes { get; set; }
        // public MovieSummary[] RelatedMovies { get; set; }
    }
    public partial class MovieBrowserWindow : Window
    {
        public MovieBrowserWindow()
        {
            InitializeComponent();
            this.Loaded += MovieBrowserWindow_Loaded;
        }

        private async void MovieBrowserWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await webView.EnsureCoreWebView2Async(null);
                webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                // webView.CoreWebView2.OpenDevToolsWindow(); 
                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebContent", "MovieBrowser.html");
                if (!File.Exists(htmlPath))
                {
                    return;
                }
                webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);

                webView.CoreWebView2.DOMContentLoaded += CoreWebView2_DOMContentLoaded;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Đã xảy ra lỗi khi tải trình duyệt phim: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CoreWebView2_DOMContentLoaded(object sender, CoreWebView2DOMContentLoadedEventArgs e)
        {
            webView.CoreWebView2.DOMContentLoaded -= CoreWebView2_DOMContentLoaded;
            await LoadHomePageData();
        }

        private async Task LoadHomePageData()
        {
            try
            {
                var homeData = new HomePageData
                {
                    NewlyUpdated = new[]
                    {
                        new MovieSummary { Id = "1", Title = "Tiên Nghịch", PosterUrl = "https://i.imgur.com/xTbsmva.jpeg", EpisodeInfo = "105/128 [4K]" },
                        new MovieSummary { Id = "2", Title = "Đấu Phá Thương Khung", PosterUrl = "https://i.imgur.com/lO2vE7E.jpeg", EpisodeInfo = "163/209 [4K]" },
                        new MovieSummary { Id = "3", Title = "Thế Giới Hoàn Mỹ", PosterUrl = "https://i.imgur.com/83p0U2j.jpeg", EpisodeInfo = "231/234 [4K]" },
                        new MovieSummary { Id = "7", Title = "Thôn Phệ Tinh Không", PosterUrl = "https://i.imgur.com/5qfUuY2.jpeg", EpisodeInfo = "Trailer 188/208 Tới T2"},
                    }
                };

                var homeJson = JsonSerializer.Serialize(homeData);
                await webView.CoreWebView2.ExecuteScriptAsync($"window.initializeHomePage({JsonSerializer.Serialize(homeJson)})");
            }
            catch (Exception ex)
            {
                await webView.CoreWebView2.ExecuteScriptAsync($"document.getElementById('loading-indicator').innerHTML = '<h2>Lỗi tải dữ liệu.</h2>';");
            }
        }

        private async void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                var message = JsonDocument.Parse(args.WebMessageAsJson).RootElement;
                string action = message.GetProperty("action").GetString();
                switch (action)
                {
                    case "getMovieDetails":
                        {
                            string movieId = message.GetProperty("movieId").GetString();
                            var movieDetails = new MovieDetails
                            {
                                Id = movieId,
                                Title = "Thôn Phệ Tinh Không",
                                PosterUrl = "https://i.imgur.com/5qfUuY2.jpeg",
                                Status = "Đang Chiếu",
                                Genre = "CN Animation, HKHuyền Huyễn, Hiện Đại",
                                Year = 2020,
                                Synopsis = "Một ngày nọ, thế giới xuất hiện loại virus RR không rõ lai lịch...",
                                Episodes = Enumerable.Range(1, 188).Reverse().Select(i => new Episode { Id = i.ToString(), Label = i.ToString() }).ToArray()
                            };

                            var detailsJson = JsonSerializer.Serialize(movieDetails);
                            Debug.WriteLine("Đã lấy chi tiết phim. Gửi lại cho JS.");
                            await webView.CoreWebView2.ExecuteScriptAsync($"window.displayMovieDetails({JsonSerializer.Serialize(detailsJson)})");
                            break;
                        }

                    case "getStreamUrl":
                        {
                            string movieId = message.GetProperty("movieId").GetString();
                            string episodeId = message.GetProperty("episodeId").GetString();
                            Debug.WriteLine($"Yêu cầu stream cho phim ID: {movieId}, Tập: {episodeId}");
                            string streamUrl = "https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8";
                            await webView.CoreWebView2.ExecuteScriptAsync($"window.playVideoStream('{streamUrl}')");
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi khi xử lý tin nhắn từ web: {ex.Message}");
            }
        }
    }
}