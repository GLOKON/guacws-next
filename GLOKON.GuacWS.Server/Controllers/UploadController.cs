using GLOKON.GuacWS.Server.Infrastructure.Token;
using GLOKON.GuacWS.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GLOKON.GuacWS.Server.Controllers
{
    [Route("api/upload")]
    [Authorize()]
    [ApiController]
    public class UploadController : ControllerBase
    {
        private readonly TokenAuthenticationOptions tokenOptions;
        private readonly IGuacConnectionsService connectionsService;

        public UploadController(
            IOptionsMonitor<TokenAuthenticationOptions> tokenOptions,
            IGuacConnectionsService connectionsService)
        {
            this.tokenOptions = tokenOptions.CurrentValue;
            this.connectionsService = connectionsService;
        }

        [HttpPost("connection/{id}")]
        public async Task<IActionResult> UploadAsync(string id, List<IFormFile> files, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(id, out Guid connectionId))
            {
                return Unauthorized();
            }

            if (connectionsService.TryGetConnection(connectionId, out var connection) && !string.IsNullOrEmpty(connection.UserDrive) && files.Count > 0)
            {
                ConnectionProfile profile = JsonSerializer.Deserialize<ConnectionProfile>(HttpContext.User.FindFirstValue(tokenOptions.TokenClaimName), tokenOptions.TokenSerializerOptions);

                // If our IDs match
                if (!string.IsNullOrEmpty(profile.Id) && connection.ConnectionProfile.Id == profile.Id)
                {
                    try
                    {
                        foreach (var formFile in files)
                        {
                            await SaveFormFileAsync(connection.UserDrive, GetSafeFileName(formFile.FileName), formFile, cancellationToken);
                        }

                        return Ok();
                    }
                    catch (OperationCanceledException)
                    {
                        return BadRequest();
                    }
                }
            }

            return Unauthorized();
        }

        [HttpPost("distribute")]
        public async Task<IActionResult> DistributeAsync(List<IFormFile> files, CancellationToken cancellationToken)
        {
            ConnectionProfile profile = JsonSerializer.Deserialize<ConnectionProfile>(HttpContext.User.FindFirstValue(tokenOptions.TokenClaimName), tokenOptions.TokenSerializerOptions);
            if (!string.IsNullOrEmpty(profile.Id) && !string.IsNullOrEmpty(profile.Group) && files.Count > 0 &&
                profile.Settings.TryGetValue("x-drive-distribution", out string canDistributeStr) && bool.TryParse(canDistributeStr, out bool canDistribute) && canDistribute)
            {
                List<string> userDrives = connectionsService.GetConnectionsByGroup(profile.Group)
                    .Where(connection => !string.IsNullOrEmpty(connection.UserDrive))
                    .Select(connection => connection.UserDrive)
                    .ToList();

                List<string> tempFilesToDelete = [];

                try
                {
                    foreach (var formFile in files)
                    {
                        var srcPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

                        using (var stream = System.IO.File.Create(srcPath))
                        {
                            tempFilesToDelete.Add(srcPath);
                            await formFile.CopyToAsync(stream, cancellationToken);
                        }

                        string fileName = GetSafeFileName(formFile.FileName);

                        foreach (var userDrive in userDrives)
                        {
                            try
                            {
                                string destPath = Path.Combine(userDrive, fileName);
                                System.IO.File.Copy(srcPath, destPath, true);
                            }
                            catch
                            {
                                // Safely ignore IO, as if we were not able to perform IO, its likely the file didnt exist in the first place
                            }
                        }
                    }

                    return Ok();
                }
                catch (OperationCanceledException)
                {
                    return BadRequest();
                }
                finally
                {
                    foreach (var file in tempFilesToDelete)
                    {
                        try
                        {
                            System.IO.File.Delete(file);
                        }
                        catch
                        {
                            // Safely ignore IO, as if we were not able to perform IO, its likely the file didnt exist in the first place
                        }
                    }
                }
            }

            return Unauthorized();
        }

        // formFile.FileName is client-controlled; Path.GetFileName strips any directory
        // components (e.g. "../../etc/passwd") so it can't be used to write outside the
        // target drive directory.
        private static string GetSafeFileName(string fileName)
        {
            string safeFileName = Path.GetFileName(fileName);
            return string.IsNullOrEmpty(safeFileName) ? Guid.NewGuid().ToString() : safeFileName;
        }

        private static async Task SaveFormFileAsync(string destDir, string safeFileName, IFormFile formFile, CancellationToken cancellationToken)
        {
            string destPath = Path.Combine(destDir, safeFileName);

            using var stream = System.IO.File.Create(destPath);
            await formFile.CopyToAsync(stream, cancellationToken);
        }
    }
}
