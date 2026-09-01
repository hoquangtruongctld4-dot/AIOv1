
using Microsoft.AspNetCore.SignalR.Client;
using subphimv1.Models;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace subphimv1.Services
{
    public class ChatService
    {
        private HubConnection _hubConnection;
        public event Action<ChatMessage> MessageReceived;

        public async Task StartAsync(string currentUsername)
        {
            string hubUrl = "https://feedbackservice.fly.dev/chatHub";

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<string, string>("ReceiveMessage", (user, message) =>
            {

            });

            try
            {
                await _hubConnection.StartAsync();
            }
            catch (Exception ex)
            {

            }
        }

        public async Task SendMessageAsync(string username, string message)
        {
            if (_hubConnection.State == HubConnectionState.Connected)
            {

                await ApiService.SubmitFeedbackAsync(username, "Chat", message);
            }
            else
            {
            }
        }
        public async Task StopAsync()
        {
            if (_hubConnection != null)
            {
                await _hubConnection.StopAsync();
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }
        }
    }
}