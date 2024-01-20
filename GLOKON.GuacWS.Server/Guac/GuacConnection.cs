using GLOKON.GuacWS.Server.Cipher;
using GLOKON.GuacWS.Server.Guac.Parameters;
using GLOKON.GuacWS.Server.Infrastructure;
using GLOKON.GuacWS.Server.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GLOKON.GuacWS.Server.Guac
{
    internal class GuacConnection
    {
        private readonly WebSocketConnection webSocket;
        private readonly GuacDClient guacD;
        private readonly GuacOptions options;
        private readonly ILogger<GuacConnection> logger;
        private readonly SymmetricCipher cipher;
        private readonly ConcurrentQueue<string> pendingWebSocketMessages = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> pendingGuacDMessages = new ConcurrentQueue<string>();

        private string guacDBuffer = string.Empty;
        private bool hasActivity;
        private bool handshakeReplySent;
        private ConnectionProfile connectionProfile;
        private CancellationTokenSource cts;

        public GuacConnection(WebSocketConnection webSocket, GuacDClient guacD, GuacOptions options, ILogger<GuacConnection> logger) {
            this.webSocket = webSocket;
            this.guacD = guacD;
            this.options = options;
            this.logger = logger;
            switch (options.Cipher.Type)
            {
                case CipherType.AES:
                    cipher = new SymmetricCipher(Aes.Create(), options.Cipher.Mode, options.Cipher.BlockSize);
                    break;
                case CipherType.DES:
                    cipher = new SymmetricCipher(DES.Create(), options.Cipher.Mode, options.Cipher.BlockSize);
                    break;
                case CipherType.RC2:
                    cipher = new SymmetricCipher(RC2.Create(), options.Cipher.Mode, options.Cipher.BlockSize);
                    break;
                case CipherType.Rijndael:
                    cipher = new SymmetricCipher(Rijndael.Create(), options.Cipher.Mode, options.Cipher.BlockSize);
                    break;
                case CipherType.TripleDES:
                    cipher = new SymmetricCipher(TripleDES.Create(), options.Cipher.Mode, options.Cipher.BlockSize);
                    break;
            }
        }

        public void Dispose()
        {
            guacD.Dispose();
            webSocket.Dispose();
        }

        public async Task StartAsync(HttpContext context)
        {
            logger.LogDebug("[{0}] Starting GuacWS Connection", guacD.Id);

            connectionProfile = ParseToken(context.Request.Query)?.Connection;

            if (connectionProfile.Settings.TryGetValue("drive-path", out string userDrive))
            {
                connectionProfile.Settings["drive-path"] = CreateUserDrive(userDrive);
            }

            webSocket.ReceiveText += HandleWebSocketMessage;
            guacD.ReceiveText += HandleGuacDMessage;

            await guacD.ConnectAsync();

            StartActivityMonitor(options.Timeout);

            // Send initial message
            await SendOpCodeAsync(new string[] { "select", nameof(connectionProfile.Type).ToLower() });

            logger.LogDebug("[{0}] Started GuacWS Connection", guacD.Id);

            Task guacRx = guacD.ReceiveUntilCloseAsync();
            Task wsRx = webSocket.ReceiveUntilCloseAsync();
            await Task.WhenAny(guacRx, wsRx);
        }

        public async Task StopAsync()
        {
            logger.LogDebug("[{0}] Stopping GuacWS Connection", guacD.Id);

            StopActivityMonitor();

            webSocket.ReceiveText -= HandleWebSocketMessage;
            guacD.ReceiveText -= HandleGuacDMessage;

            await guacD.CloseAsync();
            await webSocket.CloseAsync();

            if (connectionProfile.Settings.TryGetValue("drive-path", out string userDrive))
            {
                DeleteUserDrive(userDrive);
            }

            logger.LogDebug("[{0}] Stopped GuacWS Connection", guacD.Id);
        }

        private string CreateUserDrive(string userPath)
        {
            string finalUserPath = Path.Combine(options.UserDriveRoot, userPath);

            // Delete user path, start fresh
            DeleteUserDrive(finalUserPath);
            Directory.CreateDirectory(finalUserPath);

            return finalUserPath;
        }

        private void DeleteUserDrive(string userPath)
        {
            if (Directory.Exists(userPath))
            {
                Directory.Delete(userPath, true);
            }
        }

        private async Task HandleWebSocketMessage(object sender, string message)
        {
            UpdateActivity();

            if (handshakeReplySent)
            {
                while (pendingWebSocketMessages.Count > 0)
                {
                    if (pendingWebSocketMessages.TryDequeue(out string pendingMessage))
                    {
                        await SendToGuacD(pendingMessage, CancellationToken.None);
                    }
                }

                await SendToGuacD(message, CancellationToken.None);
            }
            else
            {
                pendingWebSocketMessages.Enqueue(message);
            }
        }

        private async Task HandleGuacDMessage(object sender, string message)
        {
            guacDBuffer += message;
            UpdateActivity();

            int endOfLastMessageIndex = guacDBuffer.LastIndexOf(";");
            if (endOfLastMessageIndex != -1)
            {
                string rawChunks = guacDBuffer.Substring(0,  endOfLastMessageIndex + 1);
                guacDBuffer = guacDBuffer.Substring(endOfLastMessageIndex + 1);

                foreach (string messageChunk in rawChunks.Split(";"))
                {
                    string completeChunk = messageChunk + ";";

                    if (handshakeReplySent)
                    {
                        await SendToWebSocket(completeChunk, CancellationToken.None);
                    }
                    else
                    {
                        await SendHandshakeReplyAsync(completeChunk);
                    }
                }
            }
        }

        private async Task SendHandshakeReplyAsync(string handshake)
        {
            await SendOpCodeAsync(new string[] {
                "size",
                GetConnectionSetting("width"),
                GetConnectionSetting("height"),
                GetConnectionSetting("dpi")
            });

            await SendOpCodeAsync(new string[] { "audio", GetConnectionSetting("audio") });
            await SendOpCodeAsync(new string[] { "video", GetConnectionSetting("video") });
            await SendOpCodeAsync(new string[] { "image", GetConnectionSetting("image") });

            string[] parameterRequests = handshake.Split(',');
            IList<string> parameterReplies = new List<string>();

            foreach (string  parameterRequest in parameterRequests)
            {
                string parameter = parameterRequest;
                if (parameterRequest.StartsWith("VERSION_"))
                {
                    parameter = "protocol_version";
                }

                parameterReplies.Add(GetConnectionSetting(parameter));
            }

            await SendOpCodeAsync(parameterReplies.ToArray());

            handshakeReplySent = true;
        }

        private string GetConnectionSetting(string parameter)
        {
            return connectionProfile.Settings.GetValueOrDefault(parameter, null);
        }

        private GuacToken ParseToken(IQueryCollection query)
        {
            if (!query.TryGetValue("token", out StringValues values))
            {
                throw new ArgumentNullException("token", "Token is missing from the query string");
            }

            string tokenValue = Encoding.UTF8.GetString(Convert.FromBase64String(values.ToString()));
            GuacToken parsedToken = JsonSerializer.Deserialize<GuacToken>(tokenValue);

            ISet<string> allowedQueryParams = new HashSet<string>();

            if (options.AllowedParameters != null)
            {
                string connectionType = nameof(parsedToken.Connection.Type).ToLower();

                if (options.AllowedParameters.Global != null)
                {
                    allowedQueryParams.UnionWith(options.AllowedParameters.Global);
                }

                if (options.AllowedParameters.Connection != null &&
                    options.AllowedParameters.Connection.TryGetValue(connectionType, out HashSet<string> allowedConnParams))
                {
                    allowedQueryParams.UnionWith(allowedConnParams);
                }
            }

            // Add parameters that are not in the token, if permitted
            query
                .Where(queryPair =>
                {
                    // Token is a reserved keyword
                    return queryPair.Key != "token" && allowedQueryParams.Contains(queryPair.Key);
                })
                .ToList()
                .ForEach(queryPair => 
                {
                    parsedToken.Connection.Settings[queryPair.Key] = queryPair.Value.ToString();
                });

            return parsedToken;
        }

        private async Task SendOpCodeAsync(string[] parameters)
        {
            string formattedOpCode = FormatOpCode(parameters);
            logger.LogDebug("[{0}] Sending Guac Operation: {1}", guacD.Id, formattedOpCode);
            await guacD.SendAsync(formattedOpCode, CancellationToken.None);
        }

        private string FormatOpCode(string[] opCodeParts)
        {
            string[] formattedOpCodes = opCodeParts.Select(opCodePart =>
            {
                string opCode = opCodePart ?? string.Empty;

                return string.Format("{0}.{1}", opCode.Length, opCode);
            }).ToArray();

            return string.Join(",", formattedOpCodes) + ";";
        }

        private async Task SendToGuacD(string message, CancellationToken cancellationToken)
        {
            try
            {
                logger.LogDebug("[{0}] WS >> GUAC: {1}", guacD.Id, message);
                await guacD.SendAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{0}] Problem sending to GuacD", guacD.Id);
            }
        }

        private async Task SendToWebSocket(string message, CancellationToken cancellationToken)
        {
            try
            {
                logger.LogDebug("[{0}] GUAC >> WS: {1}", webSocket.Id, message);
                await webSocket.SendAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{0}] Problem sending to WebSocket", webSocket.Id);
            }
        }

        private void UpdateActivity()
        {
            hasActivity = true;
        }

        private void StartActivityMonitor(int timeout)
        {
            if (cts != null)
            {
                return;
            }

            cts = new CancellationTokenSource();
            UpdateActivity();

            Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(timeout);

                    if (!hasActivity)
                    {
                        await StopAsync();
                    }

                    hasActivity = false;
                }
            });
        }

        private void StopActivityMonitor()
        {
            try
            {
                cts?.Cancel();
                cts?.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{0}] Failed to stop GuacWS activity monitor", guacD.Id);
            }
            finally
            {
                cts = null;
            }
        }
    }
}
