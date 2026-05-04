using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// --- Модели ---
public class Group
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }
}

public class Channel
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }
}

public class ChannelsAccessOptions
{
    [JsonProperty("channelsArchiveAllowed")]
    public List<string>? ChannelsArchiveAllowed { get; set; }

    [JsonProperty("channelsRealtimeAllowed")]
    public List<string>? ChannelsRealtimeAllowed { get; set; }
}

public class GroupDetails
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("channelsAccessOptions")]
    public ChannelsAccessOptions? ChannelsAccessOptions { get; set; }
}

public class Folder
{
    [JsonProperty("Id")]
    public string? Id { get; set; }

    [JsonProperty("Name")]
    public string? Name { get; set; }

    [JsonProperty("ChildChannels")]
    public List<string>? ChildChannels { get; set; } = new List<string>();

    [JsonProperty("ChildSecObjects")]
    public List<Folder>? ChildFolders { get; set; } = new List<Folder>();
}

public class GroupCameraAccessByCamera
{
    [JsonProperty("Группа")]
    public string? Group { get; set; }

    [JsonProperty("Архив")]
    public List<string>? Archive { get; set; } = new List<string>();

    [JsonProperty("Наблюдение")]
    public List<string>? Realtime { get; set; } = new List<string>();
}

public class GroupCameraAccess
{
    [JsonProperty("Группа")]
    public string? Group { get; set; }

    [JsonProperty("Папки (Архив) - полный доступ")]
    public List<string>? ArchiveFullAccess { get; set; } = new List<string>();

    [JsonProperty("Папки (Архив) - частичный доступ")]
    public List<string>? ArchivePartialAccess { get; set; } = new List<string>();

    [JsonProperty("Папки (Наблюдение) - полный доступ")]
    public List<string>? RealtimeFullAccess { get; set; } = new List<string>();

    [JsonProperty("Папки (Наблюдение) - частичный доступ")]
    public List<string>? RealtimePartialAccess { get; set; } = new List<string>();
}

public class DetailedFolderAccess
{
    public string? FolderName { get; set; }
    public List<string> AccessibleRealtimeCameras { get; set; } = new List<string>();
    public List<string> AccessibleArchiveCameras { get; set; } = new List<string>();
}

public class GroupDetailedAccessReport
{
    public string? Group { get; set; }
    public List<string> FullRealtimeFolders { get; set; } = new List<string>();
    public List<string> FullArchiveFolders { get; set; } = new List<string>();
    public List<DetailedFolderAccess> PartialRealtimeFolders { get; set; } = new List<DetailedFolderAccess>();
    public List<DetailedFolderAccess> PartialArchiveFolders { get; set; } = new List<DetailedFolderAccess>();
}

class Program
{
    static bool IsDebug = false;

    static async Task Main(string[] args)
    {
        if (args.Length > 0 && args[0].ToLower() == "debug")
        {
            IsDebug = true;
        }

        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        string username = "";
        string password = "";
        string passwordHash = "";

        while (true)
        {
            Console.WriteLine("=== Анализ прав доступа к камерам ===");
            Console.WriteLine();

            var ip = ReadInput("Введите IP-адрес сервера (например, 192.168.1.100:8080): ");
            if (!ip.StartsWith("http")) ip = $"http://{ip}";

            // --- Ввод логина ---
            if (!IsDebug)
            {
                Console.Write("Введите логин: ");
                var loginInput = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(loginInput))
                {
                    Console.WriteLine("Логин не может быть пустым.\n");
                    continue;
                }
                if (loginInput.ToLower() == "debug")
                {
                    IsDebug = true;
                    Console.WriteLine("Режим отладки включён.\n");
                    continue;
                }
                username = loginInput;
            }
            else
            {
                username = ReadInput("Введите логин: ");
            }

            password = ReadPassword("Введите пароль: ");
            passwordHash = ComputeMD5(password);

            if (IsDebug)
            {
                Console.WriteLine($"MD5 хэш пароля: {passwordHash}");
            }

            object? result = null;
            string baseFilename = "";
            string format = "";

            Console.WriteLine("\nВыберите режим:");
            Console.WriteLine("1) Группы → Камеры");
            Console.WriteLine("2) Группы → Папки (детализация по камерам)");
            var modeInput = Console.ReadLine()?.Trim();
            bool byCameras = modeInput == "1";

            if (modeInput != "1" && modeInput != "2")
            {
                Console.WriteLine("Неверный выбор. Введите 1 или 2.\n");
                continue;
            }

            // --- Выбор формата ---
            if (byCameras)
            {
                format = ReadInput("Выберите формат (txt, json, csv): ").ToLower();
                if (!new[] { "json", "txt", "csv" }.Contains(format))
                {
                    Console.WriteLine("Неверный формат. Поддерживаются: json, txt, csv.\n");
                    if (ShouldExit()) return;
                    continue;
                }
                baseFilename = "group_camera_mapping";
            }
            else
            {
                format = "txt";
                Console.WriteLine("Режим 'Группы → Папки' поддерживает только текстовый формат (txt).");
                baseFilename = "group_folder_detailed";
            }

            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "Results");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            try
            {
                Console.WriteLine("\nПолучаем список групп...");
                var groups = await GetGroups(client, ip, username, passwordHash);
                if (groups == null || !groups.Any())
                {
                    Console.WriteLine("Не удалось получить группы.");
                    if (ShouldExit()) return;
                    continue;
                }
                Console.WriteLine($"Успешно: {groups.Count} групп получено.");

                Console.WriteLine("\nПолучаем список камер...");
                var channels = await GetChannels(client, ip, username, passwordHash);
                if (channels == null || !channels.Any())
                {
                    Console.WriteLine("Не удалось получить камеры.");
                    if (ShouldExit()) return;
                    continue;
                }
                Console.WriteLine($"Успешно: {channels.Count} камер получено.");

                // Карта: ID камеры → имя камеры
                var channelMap = channels
                    .Where(ch => ch.Id != null && ch.Name != null)
                    .ToDictionary(
                        ch => ch.Id!,
                        ch => ch.Name!,
                        StringComparer.OrdinalIgnoreCase);

                Console.WriteLine("\nПолучаем иерархию папок (/configex)...");
                var folders = await GetFolders(client, ip, username, passwordHash);
                if (folders == null || !folders.Any())
                {
                    Console.WriteLine("Не удалось получить папки.");
                    if (ShouldExit()) return;
                    continue;
                }
                Console.WriteLine($"Успешно: {folders.Count} корневых папок получено.");

                var allFolders = Flatten(folders).ToList();
                Console.WriteLine($"Обнаружено всего папок: {allFolders.Count}");

                string finalPath = "";

                if (byCameras)
                {
                    var cameraResult = new List<GroupCameraAccessByCamera>();
                    foreach (var group in groups.Where(g => !string.IsNullOrEmpty(g.Name) && g.Name != "Старшие администраторы"))
                    {
                        var details = await GetGroupDetails(client, ip, group.Id ?? "", username, passwordHash);
                        var archive = details?.ChannelsAccessOptions?.ChannelsArchiveAllowed?.Where(id => !string.IsNullOrEmpty(id)).ToList() ?? new List<string>();
                        var realtime = details?.ChannelsAccessOptions?.ChannelsRealtimeAllowed?.Where(id => !string.IsNullOrEmpty(id)).ToList() ?? new List<string>();

                        var archiveNames = archive
                            .Where(id => channelMap.ContainsKey(id))
                            .Select(id => channelMap[id])
                            .Distinct()
                            .OrderBy(x => x)
                            .ToList();

                        var realtimeNames = realtime
                            .Where(id => channelMap.ContainsKey(id))
                            .Select(id => channelMap[id])
                            .Distinct()
                            .OrderBy(x => x)
                            .ToList();

                        cameraResult.Add(new GroupCameraAccessByCamera
                        {
                            Group = group.Name,
                            Archive = archiveNames,
                            Realtime = realtimeNames
                        });
                    }

                    result = cameraResult;

                    // Сохранение для всех форматов — с вводом имени
                    var tempPath = Path.Combine(outputDir, $"{baseFilename}.{format}");
                    if (format == "txt")
                    {
                        await SaveDetailedCameraTextReport(cameraResult, tempPath);
                    }
                    else
                    {
                        await SaveResult(result, format, tempPath);
                    }

                    finalPath = await PromptForFilename(tempPath);
                    Console.WriteLine($"Файл сохранён как '{finalPath}'\n");
                }
                else
                {
                    var detailedReports = new List<GroupDetailedAccessReport>();
                    foreach (var group in groups.Where(g => !string.IsNullOrEmpty(g.Name) && g.Name != "Старшие администраторы"))
                    {
                        var details = await GetGroupDetails(client, ip, group.Id ?? "", username, passwordHash);
                        var archive = details?.ChannelsAccessOptions?.ChannelsArchiveAllowed?.Where(id => !string.IsNullOrEmpty(id)).ToList() ?? new List<string>();
                        var realtime = details?.ChannelsAccessOptions?.ChannelsRealtimeAllowed?.Where(id => !string.IsNullOrEmpty(id)).ToList() ?? new List<string>();

                        var archiveSet = new HashSet<string>(archive, StringComparer.OrdinalIgnoreCase);
                        var realtimeSet = new HashSet<string>(realtime, StringComparer.OrdinalIgnoreCase);

                        var report = new GroupDetailedAccessReport { Group = group.Name };

                        foreach (var folder in allFolders)
                        {
                            if (folder.ChildChannels == null || !folder.ChildChannels.Any()) continue;

                            var channelIds = folder.ChildChannels.Where(id => !string.IsNullOrEmpty(id)).ToList();

                            int allowedRealtime = channelIds.Count(id => realtimeSet.Contains(id));
                            int allowedArchive = channelIds.Count(id => archiveSet.Contains(id));

                            // Наблюдение — полный доступ
                            if (allowedRealtime == channelIds.Count && channelIds.Count > 0)
                            {
                                report.FullRealtimeFolders.Add(folder.Name ?? "Без имени");
                            }
                            // Наблюдение — частичный доступ
                            else if (allowedRealtime > 0)
                            {
                                var partialFolder = new DetailedFolderAccess { FolderName = folder.Name ?? "Без имени" };
                                foreach (var id in channelIds)
                                {
                                    if (realtimeSet.Contains(id))
                                    {
                                        channelMap.TryGetValue(id, out string? cameraName);
                                        partialFolder.AccessibleRealtimeCameras.Add(cameraName ?? id);
                                    }
                                }
                                report.PartialRealtimeFolders.Add(partialFolder);
                            }

                            // Архив — полный доступ
                            if (allowedArchive == channelIds.Count && channelIds.Count > 0)
                            {
                                report.FullArchiveFolders.Add(folder.Name ?? "Без имени");
                            }
                            // Архив — частичный доступ
                            else if (allowedArchive > 0)
                            {
                                var folderNameKey = folder.Name ?? "Без имени";
                                var partialFolder = report.PartialArchiveFolders.FirstOrDefault(f => f.FolderName == folderNameKey)
                                                  ?? new DetailedFolderAccess { FolderName = folderNameKey };

                                if (!report.PartialArchiveFolders.Contains(partialFolder))
                                {
                                    report.PartialArchiveFolders.Add(partialFolder);
                                }

                                foreach (var id in channelIds)
                                {
                                    if (archiveSet.Contains(id))
                                    {
                                        channelMap.TryGetValue(id, out string? cameraName);
                                        var displayName = cameraName ?? id;
                                        if (!partialFolder.AccessibleArchiveCameras.Contains(displayName))
                                        {
                                            partialFolder.AccessibleArchiveCameras.Add(displayName);
                                        }
                                    }
                                }
                            }
                        }

                        report.FullRealtimeFolders = report.FullRealtimeFolders.OrderBy(x => x).ToList();
                        report.FullArchiveFolders = report.FullArchiveFolders.OrderBy(x => x).ToList();
                        report.PartialRealtimeFolders = report.PartialRealtimeFolders
                            .OrderBy(f => f.FolderName)
                            .ThenBy(f => f.AccessibleRealtimeCameras.FirstOrDefault())
                            .ToList();
                        report.PartialArchiveFolders = report.PartialArchiveFolders
                            .OrderBy(f => f.FolderName)
                            .ThenBy(f => f.AccessibleArchiveCameras.FirstOrDefault())
                            .ToList();

                        detailedReports.Add(report);
                    }

                    result = detailedReports;
                    var tempPath = Path.Combine(outputDir, $"{baseFilename}.{format}");
                    await SaveDetailedTextReport(detailedReports, tempPath);
                    finalPath = await PromptForFilename(tempPath);
                    Console.WriteLine($"Файл сохранён как '{finalPath}'\n");
                }
            }
            catch (Exception ex)
            {
                if (IsDebug)
                {
                    Console.WriteLine($"Исключение: {ex}");
                }
                else
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                }
            }

            if (ShouldExit()) return;
        }
    }

    // --- Универсальный запрос имени файла ---
    static async Task<string> PromptForFilename(string tempPath)
    {
        string finalPath = tempPath;

        Console.Write("Введите имя файла (без расширения) или нажмите Enter, чтобы оставить как есть: ");
        var userInput = Console.ReadLine()?.Trim();

        if (!string.IsNullOrEmpty(userInput))
        {
            var extension = Path.GetExtension(tempPath);
            finalPath = Path.Combine(Path.GetDirectoryName(tempPath)!, $"{userInput}{extension}");

            try
            {
                if (File.Exists(finalPath))
                {
                    Console.Write($"Файл '{finalPath}' уже существует. Перезаписать? (y/n): ");
                    var confirm = Console.ReadLine()?.Trim().ToLower();
                    if (confirm == "y" || confirm == "yes")
                    {
                        File.Delete(finalPath);
                        File.Move(tempPath, finalPath);
                    }
                    else
                    {
                        Console.WriteLine("Имя файла не изменено.");
                        finalPath = tempPath;
                    }
                }
                else
                {
                    File.Move(tempPath, finalPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при переименовании: {ex.Message}");
                finalPath = tempPath;
            }
        }

        return finalPath;
    }

    static string ReadInput(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine()?.Trim() ?? "";
    }

    static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write("*");
            }
        }
        return password.ToString();
    }

    static bool ShouldExit()
    {
        while (true)
        {
            Console.Write("Завершить программу? (y/n): ");
            var input = Console.ReadLine()?.Trim().ToLower();
            if (input == "y" || input == "yes") return true;
            if (input == "n" || input == "no")
            {
                Console.WriteLine();
                return false;
            }
            Console.WriteLine("Введите 'y' для выхода или 'n' для продолжения.");
        }
    }

    static string ComputeMD5(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        using var md5 = MD5.Create();
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        byte[] hash = md5.ComputeHash(bytes);
        return string.Concat(hash.Select(b => b.ToString("x2")));
    }

    static async Task<List<Group>?> GetGroups(HttpClient client, string baseUrl, string username, string passwordHash)
    {
        try
        {
            var url = $"{baseUrl}/configure/groups";
            if (IsDebug) Console.WriteLine($"Запрос: GET {url}");

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{passwordHash}"));
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

            var response = await client.GetAsync(url);
            if (IsDebug)
            {
                Console.WriteLine($"Статус: {response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Ответ: {content}");
                }
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            if (IsDebug) Console.WriteLine($"Ответ JSON:\n{FormatJson(json)}\n");

            return JsonConvert.DeserializeObject<List<Group>>(json);
        }
        catch (Exception ex)
        {
            if (IsDebug)
            {
                Console.WriteLine($"Ошибка в GetGroups: {ex}");
            }
            else
            {
                Console.WriteLine($"Ошибка при получении групп: {ex.Message}");
            }
            return null;
        }
    }

    static async Task<List<Channel>?> GetChannels(HttpClient client, string baseUrl, string username, string passwordHash)
    {
        try
        {
            var url = $"{baseUrl}/configure/channels";
            if (IsDebug) Console.WriteLine($"Запрос: GET {url}");

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{passwordHash}"));
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

            var response = await client.GetAsync(url);
            if (IsDebug)
            {
                Console.WriteLine($"Статус: {response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Ответ: {content}");
                }
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            if (IsDebug) Console.WriteLine($"Ответ JSON:\n{FormatJson(json)}\n");

            return JsonConvert.DeserializeObject<List<Channel>>(json);
        }
        catch (Exception ex)
        {
            if (IsDebug)
            {
                Console.WriteLine($"Ошибка в GetChannels: {ex}");
            }
            else
            {
                Console.WriteLine($"Ошибка при получении камер: {ex.Message}");
            }
            return null;
        }
    }

    static async Task<GroupDetails?> GetGroupDetails(HttpClient client, string baseUrl, string groupId, string username, string passwordHash)
    {
        try
        {
            var url = $"{baseUrl}/configure/groups/{groupId}";
            if (IsDebug) Console.WriteLine($"Запрос: GET {url}");

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{passwordHash}"));
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

            var response = await client.GetAsync(url);
            if (IsDebug)
            {
                Console.WriteLine($"Статус: {response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Ответ: {content}");
                }
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            if (IsDebug) Console.WriteLine($"Ответ JSON:\n{FormatJson(json)}\n");

            return JsonConvert.DeserializeObject<GroupDetails>(json);
        }
        catch (Exception ex)
        {
            if (IsDebug)
            {
                Console.WriteLine($"Ошибка в GetGroupDetails: {ex}");
            }
            else
            {
                Console.WriteLine($"Ошибка при получении деталей группы: {ex.Message}");
            }
            return null;
        }
    }

    static async Task<List<Folder>?> GetFolders(HttpClient client, string baseUrl, string username, string passwordHash)
    {
        try
        {
            var url = $"{baseUrl}/configex?login={username}&password={passwordHash}&responsetype=json";
            if (IsDebug) Console.WriteLine($"Запрос: GET {url}");

            var response = await client.GetAsync(url);
            if (IsDebug)
            {
                Console.WriteLine($"Статус: {response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Ответ: {content}");
                }
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            if (IsDebug) Console.WriteLine($"Ответ JSON:\n{FormatJson(json)}\n");

            var root = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            if (root == null || !root.TryGetValue("RootSecObject", out object? rootSecObj) || rootSecObj == null)
                return new List<Folder>();

            var rootObj = JsonConvert.SerializeObject(rootSecObj);
            var folder = JsonConvert.DeserializeObject<Folder>(rootObj);
            return folder?.ChildFolders ?? new List<Folder>();
        }
        catch (Exception ex)
        {
            if (IsDebug)
            {
                Console.WriteLine($"Ошибка в GetFolders: {ex}");
            }
            else
            {
                Console.WriteLine($"Ошибка при загрузке папок: {ex.Message}");
            }
            return null;
        }
    }

    static IEnumerable<Folder> Flatten(List<Folder>? folders)
    {
        if (folders == null) yield break;

        foreach (var f in folders)
        {
            yield return f;
            foreach (var child in Flatten(f.ChildFolders))
                yield return child;
        }
    }

    static async Task SaveResult(object result, string format, string filename)
    {
        switch (format)
        {
            case "json":
                string json = JsonConvert.SerializeObject(result, Formatting.Indented);
                await File.WriteAllTextAsync(filename, json, Encoding.UTF8);
                break;

            case "csv":
                var csv = new StringBuilder();
                if (result is IEnumerable<object> items)
                {
                    var first = items.FirstOrDefault();
                    if (first != null)
                    {
                        var props = first.GetType().GetProperties();
                        csv.AppendLine(string.Join(";", props.Select(p => EscapeCsv(p.Name))));
                        foreach (var item in items)
                        {
                            var values = props.Select(p =>
                            {
                                var listVal = p.GetValue(item) as IEnumerable<string>;
                                return EscapeCsv(string.Join("|", listVal ?? new List<string>()));
                            });
                            csv.AppendLine(string.Join(";", values));
                        }
                    }
                }
                await File.WriteAllTextAsync(filename, csv.ToString(), Encoding.UTF8);
                break;
        }
    }

    static async Task SaveDetailedTextReport(List<GroupDetailedAccessReport> reports, string filename)
    {
        var sb = new StringBuilder();

        foreach (var report in reports)
        {
            sb.AppendLine($"Группа: {report.Group}");
            sb.AppendLine();

            if (report.FullRealtimeFolders?.Any() == true)
            {
                sb.AppendLine("Папки с полным доступом (наблюдение):");
                foreach (var folder in report.FullRealtimeFolders.OrderBy(x => x))
                {
                    sb.AppendLine($"  • {folder} (все камеры)");
                }
                sb.AppendLine();
            }

            if (report.PartialRealtimeFolders?.Any() == true)
            {
                sb.AppendLine("Папки с частичным доступом (наблюдение):");
                foreach (var folder in report.PartialRealtimeFolders.OrderBy(f => f.FolderName))
                {
                    sb.AppendLine($"  • {folder.FolderName}");
                    foreach (var cam in folder.AccessibleRealtimeCameras.OrderBy(x => x))
                    {
                        sb.AppendLine($"      - {cam}");
                    }
                }
                sb.AppendLine();
            }

            if (report.FullArchiveFolders?.Any() == true)
            {
                sb.AppendLine("Папки с полным доступом (архив):");
                foreach (var folder in report.FullArchiveFolders.OrderBy(x => x))
                {
                    sb.AppendLine($"  • {folder} (все камеры)");
                }
                sb.AppendLine();
            }

            if (report.PartialArchiveFolders?.Any() == true)
            {
                sb.AppendLine("Папки с частичным доступом (архив):");
                foreach (var folder in report.PartialArchiveFolders.OrderBy(f => f.FolderName))
                {
                    sb.AppendLine($"  • {folder.FolderName}");
                    foreach (var cam in folder.AccessibleArchiveCameras.OrderBy(x => x))
                    {
                        sb.AppendLine($"      - {cam}");
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine(new string('─', 60));
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(filename, sb.ToString(), Encoding.UTF8);
    }

    static async Task SaveDetailedCameraTextReport(List<GroupCameraAccessByCamera> reports, string filename)
    {
        var sb = new StringBuilder();

        foreach (var report in reports)
        {
            sb.AppendLine($"Группа: {report.Group}");
            sb.AppendLine();

            if (report.Archive?.Any() == true)
            {
                sb.AppendLine("Архив:");
                foreach (var cam in report.Archive.OrderBy(x => x))
                {
                    sb.AppendLine($"  - {cam}");
                }
                sb.AppendLine();
            }

            if (report.Realtime?.Any() == true)
            {
                sb.AppendLine("Наблюдение:");
                foreach (var cam in report.Realtime.OrderBy(x => x))
                {
                    sb.AppendLine($"  - {cam}");
                }
                sb.AppendLine();
            }

            sb.AppendLine(new string('─', 60));
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(filename, sb.ToString(), Encoding.UTF8);
    }

    static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n'))
        {
            value = value.Replace("\"", "\"\"");
            return $"\"{value}\"";
        }
        return value;
    }

    static string FormatJson(string json)
    {
        try
        {
            return JToken.Parse(json).ToString(Formatting.Indented);
        }
        catch
        {
            return json;
        }
    }
}
