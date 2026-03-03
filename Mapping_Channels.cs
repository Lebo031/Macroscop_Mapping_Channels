using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class Group
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }
}

public class Channel
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }
}

public class ChannelsAccessOptions
{
    [JsonProperty("channelsArchiveAllowed")]
    public List<string> ChannelsArchiveAllowed { get; set; }

    [JsonProperty("channelsRealtimeAllowed")]
    public List<string> ChannelsRealtimeAllowed { get; set; }
}

public class GroupDetails
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("channelsAccessOptions")]
    public ChannelsAccessOptions ChannelsAccessOptions { get; set; }
}

public class GroupCameraAccess
{
    [JsonProperty("Группа")]
    public string Group { get; set; }

    [JsonProperty("Архив")]
    public List<string> Archive { get; set; } = new List<string>();

    [JsonProperty("Наблюдение")]
    public List<string> Realtime { get; set; } = new List<string>();
}

class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        while (true)
        {
            Console.WriteLine("=== Скрипт для получения соответствия групп и камер ===");
            Console.WriteLine();

            var ip = ReadInput("Введите IP-адрес сервера (например, 127.0.0.1:8080): ");
            if (!ip.StartsWith("http")) ip = $"http://{ip}";

            var username = ReadInput("Введите логин: ");
            var password = ReadPassword("Введите пароль: ");
            var passwordHash = ComputeMD5(password);

            Console.WriteLine("Пароль преобразован в MD5.");

            var format = ReadInput("Выберите формат (txt, json, csv): ").ToLower();
            if (!new[] { "json", "txt", "csv" }.Contains(format))
            {
                Console.WriteLine("Неверный формат. Поддерживаются: txt, json, csv.\n");
                if (ShouldExit()) return;
                continue;
            }

            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "Results");
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{passwordHash}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            try
            {
                Console.WriteLine("\nПолучаем список групп...");
                var groups = await GetGroups(client, ip);
                if (groups == null || !groups.Any())
                {
                    Console.WriteLine("❌ Не удалось получить группы или список пуст.");
                    if (ShouldExit()) return;
                    continue;
                }
                Console.WriteLine($"Успешно: {groups.Count} групп получено.");

                Console.WriteLine("\nПолучаем список камер...");
                var channels = await GetChannels(client, ip);
                if (channels == null || !channels.Any())
                {
                    Console.WriteLine("❌ Не удалось получить камеры или список пуст.");
                    if (ShouldExit()) return;
                    continue;
                }
                Console.WriteLine($"Успешно: {channels.Count} камер получено.");

                var channelMap = channels.ToDictionary(ch => ch.Id, ch => ch.Name, StringComparer.OrdinalIgnoreCase);

                Console.WriteLine("\nАнализируем права доступа групп...");
                var result = new List<GroupCameraAccess>();

                foreach (var group in groups)
                {
                    if (string.IsNullOrEmpty(group.Name)) continue;
                    if (group.Name == "Старшие администраторы") continue;

                    var details = await GetGroupDetails(client, ip, group.Id);
                    var archive = details?.ChannelsAccessOptions?.ChannelsArchiveAllowed?.Where(id => !string.IsNullOrEmpty(id)).ToList() ?? new List<string>();
                    var realtime = details?.ChannelsAccessOptions?.ChannelsRealtimeAllowed?.Where(id => !string.IsNullOrEmpty(id)).ToList() ?? new List<string>();

                    var archiveNames = archive
                        .Where(id => channelMap.ContainsKey(id))
                        .Select(id => channelMap[id])
                        .Distinct()
                        .ToList();

                    var realtimeNames = realtime
                        .Where(id => channelMap.ContainsKey(id))
                        .Select(id => channelMap[id])
                        .Distinct()
                        .ToList();

                    result.Add(new GroupCameraAccess
                    {
                        Group = group.Name,
                        Archive = archiveNames,
                        Realtime = realtimeNames
                    });

                    Console.WriteLine($"Обработано: {group.Name} | Архив: {archiveNames.Count}, Наблюдение: {realtimeNames.Count}");
                }

                Console.WriteLine();
                Console.WriteLine("=== Сохранение результатов ===");
                Console.WriteLine();

                var baseFilename = $"group_camera_mapping.{format}";
                var tempFilename = Path.Combine(outputDir, baseFilename);
                var finalFilename = tempFilename;

                switch (format)
                {
                    case "json":
                        string json = JsonConvert.SerializeObject(result, Formatting.Indented);
                        await File.WriteAllTextAsync(tempFilename, json, Encoding.UTF8);
                        break;

                    case "txt":
                        var txt = new StringBuilder();
                        foreach (var item in result)
                        {
                            txt.AppendLine($"Группа: {item.Group}");
                            txt.AppendLine($" Архив: {string.Join(", ", item.Archive)}");
                            txt.AppendLine($" Наблюдение: {string.Join(", ", item.Realtime)}");
                            txt.AppendLine();
                        }
                        await File.WriteAllTextAsync(tempFilename, txt.ToString(), Encoding.UTF8);
                        break;

                    case "csv":
                        var csv = new StringBuilder();
                        csv.AppendLine("Группа;Архив;Наблюдение");
                        foreach (var item in result)
                        {
                            var archiveStr = string.Join("|", item.Archive);
                            var realtimeStr = string.Join("|", item.Realtime);
                            csv.AppendLine($"{EscapeCsv(item.Group)};{EscapeCsv(archiveStr)};{EscapeCsv(realtimeStr)}");
                        }
                        await File.WriteAllTextAsync(tempFilename, csv.ToString(), Encoding.UTF8);
                        break;
                }

                Console.Write("Введите имя файла (без расширения) или нажмите Enter, чтобы оставить как есть: ");
                var userInput = Console.ReadLine()?.Trim();

                if (!string.IsNullOrEmpty(userInput))
                {
                    var extension = Path.GetExtension(tempFilename);
                    finalFilename = Path.Combine(outputDir, $"{userInput}{extension}");

                    try
                    {
                        if (File.Exists(finalFilename))
                        {
                            Console.Write($"Файл '{finalFilename}' уже существует. Перезаписать? (y/n): ");
                            var confirm = Console.ReadLine()?.Trim().ToLower();
                            if (confirm != "y" && confirm != "yes")
                            {
                                Console.WriteLine("Имя файла не изменено.");
                                finalFilename = tempFilename;
                            }
                            else
                            {
                                File.Delete(finalFilename);
                                File.Move(tempFilename, finalFilename);
                            }
                        }
                        else
                        {
                            File.Move(tempFilename, finalFilename);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка при переименовании: {ex.Message}");
                        finalFilename = tempFilename;
                    }
                }

                Console.WriteLine($"Файл сохранён как '{finalFilename}'\n");
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("401"))
            {
                Console.WriteLine("❌ Неверный логин или пароль (проверьте MD5).\n");
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("403"))
            {
                Console.WriteLine("❌ Доступ запрещён. Проверьте права пользователя.\n");
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("404"))
            {
                Console.WriteLine("❌ API не найдено. Проверьте адрес сервера.\n");
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("❌ Таймаут подключения. Проверьте сетевое соединение или IP-адрес.\n");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"❌ Ошибка HTTP: {ex.Message}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Неизвестная ошибка: {ex.Message}\n");
            }

            if (ShouldExit()) return;
        }
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
            if (input == "y" || input == "yes")
                return true;
            if (input == "n" || input == "no")
            {
                Console.WriteLine();
                return false;
            }
            Console.WriteLine("Введите 'y' для выхода или 'n' для продолжения.");
        }
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

    static string ComputeMD5(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        using var md5 = MD5.Create();
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = md5.ComputeHash(inputBytes);

        var sb = new StringBuilder();
        foreach (byte b in hashBytes)
        {
            sb.Append(b.ToString("x2")); // hex, lowercase
        }
        return sb.ToString();
    }

    static async Task<List<Group>> GetGroups(HttpClient client, string baseUrl)
    {
        try
        {
            var response = await client.GetAsync($"{baseUrl}/configure/groups");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<List<Group>>(json);
        }
        catch
        {
            return null;
        }
    }

    static async Task<List<Channel>> GetChannels(HttpClient client, string baseUrl)
    {
        try
        {
            var response = await client.GetAsync($"{baseUrl}/configure/channels");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<List<Channel>>(json);
        }
        catch
        {
            return null;
        }
    }

    static async Task<GroupDetails> GetGroupDetails(HttpClient client, string baseUrl, string groupId)
    {
        try
        {
            var response = await client.GetAsync($"{baseUrl}/configure/groups/{groupId}");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<GroupDetails>(json);
        }
        catch
        {
            return null;
        }
    }
}
