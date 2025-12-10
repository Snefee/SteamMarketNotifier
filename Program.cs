using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(RootConfig))]
[JsonSerializable(typeof(SteamMarketPrice))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}

public class SteamMarketPrice
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("lowest_price")]
    public string? LowestPrice { get; set; }

    [JsonPropertyName("volume")]
    public string? Volume { get; set; }

    [JsonPropertyName("median_price")]
    public string? MedianPrice { get; set; }
}

public class PresetConfig
{
    public string ItemName { get; set; } = string.Empty;
    public int CurrencyType { get; set; }
    public string NtfyTopic { get; set; } = string.Empty;
    public float PriceRiseThreshold { get; set; }
    public float PriceDropThreshold { get; set; }
}

public class RootConfig
{
    public int ActivePreset { get; set; }
    public List<PresetConfig> Presets { get; set; } = new List<PresetConfig>();
}

public class Program
{
    private static readonly HttpClient client = new HttpClient();

    private static int currencyType = 1; // Default currency = USD
    private static string apiUrl => $"https://steamcommunity.com/market/priceoverview/?currency={currencyType}&appid=730&market_hash_name={itemName}";

    // Tracking update interval in seconds
    const int updateIntervalSeconds = 300;

    private static int activePreset = 1;
    private static float priceRiseThreshold = 0.0f;
    private static float priceDropThreshold = 0.0f;
    private static string ntfyTopic = string.Empty;
    private static bool isNtfyEnabled => !string.IsNullOrEmpty(ntfyTopic);
    private static string itemName = string.Empty;

    private static float? previousLowestPrice = null;
    private static float? previousMedianPrice = null;
    private static bool isAlertSent = false;

    // === Application version ===
    private static readonly string appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private static readonly string configFile = Path.Combine(AppContext.BaseDirectory, "config.json");
    private static string? _tempLogFilePath;

    private static readonly Dictionary<string, int> steamCurrencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "USD", 1 }, { "GBP", 2 }, { "EUR", 3 }, { "CHF", 4 }, { "RUB", 5 },
        { "PLN", 6 }, { "BRL", 7 }, { "JPY", 8 }, { "NOK", 9 }, { "IDR", 10 },
        { "MYR", 11 }, { "PHP", 12 }, { "SGD", 13 }, { "THB", 14 }, { "VND", 15 },
        { "KRW", 16 }, { "UAH", 18 }, { "MXN", 19 }, { "CAD", 20 }, { "AUD", 21 },
        { "NZD", 22 }, { "CNY", 23 }, { "INR", 24 }, { "CLP", 25 }, { "PEN", 26 },
        { "COP", 27 }, { "ZAR", 28 }, { "HKD", 29 }, { "TWD", 30 }, { "SAR", 31 },
        { "AED", 32 }, { "ILS", 35 }, { "KZT", 37 }, { "KWD", 38 }, { "QAR", 39 },
        { "CRC", 40 }, { "UYU", 41 }
    };

    private static readonly Dictionary<int, string> currencyIdToCode = new Dictionary<int, string>();

    static Program()
    {
        foreach (var entry in steamCurrencies)
        {
            if (!currencyIdToCode.ContainsKey(entry.Value))
            {
                currencyIdToCode.Add(entry.Value, entry.Key);
            }
        }
    }


    public static async Task Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
    MainMenu:
        previousLowestPrice = null;
        previousMedianPrice = null;

        // Load configuration from file
        var config = LoadConfig();
        if (config != null)
            while (true)
            {
                Console.Clear();

                if (activePreset >= 1 && activePreset <= config.Presets.Count)
                {
                    var currentPresetConfig = config.Presets[activePreset - 1];

                    itemName = currentPresetConfig.ItemName;
                    currencyType = currentPresetConfig.CurrencyType;
                    ntfyTopic = currentPresetConfig.NtfyTopic;
                    priceRiseThreshold = currentPresetConfig.PriceRiseThreshold;
                    priceDropThreshold = currentPresetConfig.PriceDropThreshold;
                }
                else
                {
                    // Handle invalid preset index
                }


                for (int i = 1; i <= 5; i++)
                {
                    if (activePreset == i)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                    }
                    Console.Write($" Preset {i} ");
                    Console.ResetColor();

                    if (i < 5)
                    {
                        Console.Write("|");
                    }
                }

                Console.WriteLine("\n\n--- Selected configuration ---");
                Console.WriteLine($"Tracked Item: {itemName.Replace("%20", " ").Replace("%7C", "|").Replace("%E2%98%85", "★").Replace("%E2%84%A2", "™").Replace("%28", "(").Replace("%29", ")")}");
                Console.WriteLine($"Currency: {currencyIdToCode.GetValueOrDefault(currencyType, "Unknown/Not Set")}");
                Console.WriteLine($"Ntfy Topic: {ntfyTopic}");
                Console.WriteLine($"Price Rise Threshold: {priceRiseThreshold}");
                Console.WriteLine($"Price Drop Threshold: {priceDropThreshold}");
                Console.WriteLine("-----------------------------------");

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\nUse Left/Right arrow keys [or A/D] to switch presets.");
                Console.WriteLine("Press C to change selected preset's configuration");
                Console.WriteLine("Press Enter/Space to start tracking.");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"App Version: {appVersion}");
                Console.ResetColor();

                var keyInfo = Console.ReadKey(true);

                if (keyInfo.Key == ConsoleKey.LeftArrow || keyInfo.Key == ConsoleKey.A)
                {
                    activePreset = (activePreset == 1) ? 5 : activePreset - 1;
                }
                else if (keyInfo.Key == ConsoleKey.RightArrow || keyInfo.Key == ConsoleKey.D)
                {
                    activePreset = (activePreset == 5) ? 1 : activePreset + 1;
                }
                else if (keyInfo.Key == ConsoleKey.C)
                {
                    // Enable configuration mode
                    var originalPreset = config.Presets[activePreset - 1];
                    var tempPreset = new PresetConfig
                    {
                        ItemName = originalPreset.ItemName,
                        CurrencyType = originalPreset.CurrencyType,
                        NtfyTopic = originalPreset.NtfyTopic,
                        PriceRiseThreshold = originalPreset.PriceRiseThreshold,
                        PriceDropThreshold = originalPreset.PriceDropThreshold
                    };

                    try
                    {
                        // Pass the temporary object to the configuration method
                        ConfigurePresetDetails(tempPreset, activePreset);

                        config.Presets[activePreset - 1] = tempPreset;
                        SaveConfig(config);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("\nConfiguration saved successfully!");
                        Console.ResetColor();
                        await Task.Delay(1000);
                    }
                    catch (OperationCanceledException)
                    {
                        // Handle the ESC press
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("\n\nConfiguration cancelled. No changes were made.");
                        Console.ResetColor();
                        await Task.Delay(1000);
                    }
                }
                else if (keyInfo.Key == ConsoleKey.Enter || keyInfo.Key == ConsoleKey.Spacebar)
                {
                    // Save the last selected preset before starting
                    config.ActivePreset = activePreset;
                    SaveConfig(config);
                    break;
                }
            }
        else
        {
            var newRootConfig = new RootConfig { ActivePreset = 1 };
            for (int i = 0; i < 5; i++)
            {
                newRootConfig.Presets.Add(new PresetConfig { ItemName = "Not Set", CurrencyType = 1 });
            }

            var firstPreset = newRootConfig.Presets[0];
            try
            {
                ConfigurePresetDetails(firstPreset, 1, true);
                SaveConfig(newRootConfig);
                Console.WriteLine("Configuration saved!");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Setup cancelled, exiting.");
                return;
            }

            await Task.Delay(1000);
            goto MainMenu;
        }

        try
        {
            _tempLogFilePath = Path.GetTempFileName();
            await File.WriteAllTextAsync(_tempLogFilePath, "Timestamp,LowestPrice,MedianPrice,Volume\n");

            while (true)
            {
                await FetchAndDisplayPrice();

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\nPress E to export data, ESC to return to menu");

                int remainingSeconds = updateIntervalSeconds;

                while (remainingSeconds > 0)
                {
                    TimeSpan timeSpan = TimeSpan.FromSeconds(remainingSeconds);
                    Console.Write($"\rNext update in: {timeSpan:mm\\:ss}");

                    for (int i = 0; i < 10; i++)
                    {
                        await Task.Delay(100);
                        if (Console.KeyAvailable)
                        {
                            var keyInfo = Console.ReadKey(true);
                            if (keyInfo.Key == ConsoleKey.Escape)
                            {
                                Console.ResetColor();
                                goto MainMenu;
                            }
                            else if (keyInfo.Key == ConsoleKey.E)
                            {
                                ExportDataToCsv();
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine("Data exported successfully!");
                                Console.ResetColor();
                                Console.ForegroundColor = ConsoleColor.Yellow;
                            }
                        }
                    }
                    remainingSeconds--;
                }
                Console.ResetColor();
                Console.Write(new string(' ', Console.WindowWidth - 1) + "\r");
            }
        }
        finally
        {
            if (_tempLogFilePath != null && File.Exists(_tempLogFilePath))
            {
                File.Delete(_tempLogFilePath);
                _tempLogFilePath = null;
            }
        }
    }

    private static void ConfigurePresetDetails(PresetConfig presetToConfigure, int presetNumber, bool isFirstRun = false)
    {
        int selectedOption = 0;
        string[] menuOptions = { "Item Name", "Currency", "Ntfy Topic", "Price Rise Threshold", "Price Drop Threshold", "Save and Exit" };

        while (true)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Yellow;

            if (isFirstRun)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Welcome to the Steam Market Notifier!");
                Console.WriteLine("Please configure your first preset.\n");
                Console.ForegroundColor = ConsoleColor.Yellow;
            }

            Console.WriteLine($"--- Configuring Preset {presetNumber} ---");
            Console.WriteLine("Use Up/Down arrows [or W/S] to navigate, Enter/Space to edit, ESC to cancel.\n");
            Console.ResetColor();

            for (int i = 0; i < menuOptions.Length; i++)
            {
                if (i == selectedOption)
                {
                    Console.BackgroundColor = ConsoleColor.Cyan;
                    Console.ForegroundColor = ConsoleColor.Black;
                }

                if (i == menuOptions.Length - 1)
                {
                    Console.WriteLine(menuOptions[i]);
                }
                else
                {
                    string currentValue = GetValueForOption(i, presetToConfigure);
                    Console.WriteLine($"{menuOptions[i],-25}: {currentValue}");
                }

                Console.ResetColor();
            }

            var keyInfo = Console.ReadKey(true);

            switch (keyInfo.Key)
            {
                case ConsoleKey.UpArrow:
                    selectedOption = (selectedOption == 0) ? menuOptions.Length - 1 : selectedOption - 1;
                    break;
                case ConsoleKey.DownArrow:
                    selectedOption = (selectedOption == menuOptions.Length - 1) ? 0 : selectedOption + 1;
                    break;
                case ConsoleKey.W:
                    selectedOption = (selectedOption == 0) ? menuOptions.Length - 1 : selectedOption - 1;
                    break;
                case ConsoleKey.S:
                    selectedOption = (selectedOption == menuOptions.Length - 1) ? 0 : selectedOption + 1;
                    break;
                case ConsoleKey.Enter:
                case ConsoleKey.Spacebar:
                    if (selectedOption == menuOptions.Length - 1) // Save and Exit
                    {
                        return;
                    }
                    EditOption(selectedOption, presetToConfigure);
                    isFirstRun = false;
                    break;
                case ConsoleKey.Escape:
                    throw new OperationCanceledException();
            }
        }
    }

    private static string GetValueForOption(int optionIndex, PresetConfig preset)
    {
        switch (optionIndex)
        {
            case 0: return preset.ItemName.Replace("%20", " ").Replace("%7C", "|").Replace("%E2%98%85", "★").Replace("%E2%84%A2", "™").Replace("%28", "(").Replace("%29", ")");
            case 1: return currencyIdToCode.GetValueOrDefault(preset.CurrencyType, "Not Set");
            case 2: return string.IsNullOrEmpty(preset.NtfyTopic) ? "Not Set" : preset.NtfyTopic;
            case 3: return preset.PriceRiseThreshold == 0.0f ? "Not Set" : preset.PriceRiseThreshold.ToString(CultureInfo.InvariantCulture);
            case 4: return preset.PriceDropThreshold == 0.0f ? "Not Set" : preset.PriceDropThreshold.ToString(CultureInfo.InvariantCulture);
            default: return "";
        }
    }

    private static void EditOption(int optionIndex, PresetConfig presetToConfigure)
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"--- Editing {((string[])["Item Name", "Currency", "Ntfy Topic", "Price Rise Threshold", "Price Drop Threshold"])[optionIndex]} ---\n");
        Console.ResetColor();

        try
        {
            switch (optionIndex)
            {
                case 0: EditItemName(presetToConfigure); break;
                case 1: EditCurrency(presetToConfigure); break;
                case 2: EditNtfyTopic(presetToConfigure); break;
                case 3: EditPriceRiseThreshold(presetToConfigure); break;
                case 4: EditPriceDropThreshold(presetToConfigure); break;
            }
        }
        catch (OperationCanceledException)
        {
            // Do nothing, just return to the main config menu
        }
    }

    private static void EditItemName(PresetConfig presetToConfigure)
    {
        Console.WriteLine("Write the exact item name or paste a full Steam Market link:");
        string itemInput = ReadLineWithCancel();

        if (!string.IsNullOrEmpty(itemInput))
        {
            const string urlPrefix = "https://steamcommunity.com/market/listings/730/";
            if (itemInput.StartsWith(urlPrefix, StringComparison.OrdinalIgnoreCase))
            {
                presetToConfigure.ItemName = itemInput.Substring(urlPrefix.Length);
            }
            else
            {
                presetToConfigure.ItemName = itemInput.Replace(" ", "%20").Replace("|", "%7C").Replace("★", "%E2%98%85").Replace("™", "%E2%84%A2").Replace("(", "%28").Replace(")", "%29");
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Item name cannot be empty. Press any key to try again.");
            Console.ResetColor();
            Console.ReadKey(true);
            EditItemName(presetToConfigure); // Retry
        }
    }

    private static void EditCurrency(PresetConfig presetToConfigure)
    {
        Console.WriteLine("Enter the 3-letter currency code (e.g., USD, EUR, JPY).");
        Console.WriteLine("For a full list of supported currencies, see: https://partner.steamgames.com/doc/store/pricing/currencies");

        string currencyInput = ReadLineWithCancel();
        if (steamCurrencies.TryGetValue(currencyInput, out int selectedCurrency))
        {
            presetToConfigure.CurrencyType = selectedCurrency;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid currency code. Press any key to try again.");
            Console.ResetColor();
            Console.ReadKey(true);
            EditCurrency(presetToConfigure); // Retry
        }
    }

    private static void EditNtfyTopic(PresetConfig presetToConfigure)
    {
        Console.WriteLine("Enter your Ntfy topic ID (5-64 characters), or leave empty to disable notifications.");
        Console.WriteLine("Wiki: https://github.com/Snefee/SteamMarketNotifier/wiki/NTFY-Setup");

        string ntfyInput = ReadLineWithCancel();
        if (string.IsNullOrEmpty(ntfyInput))
        {
            presetToConfigure.NtfyTopic = string.Empty;
        }
        else if (ntfyInput.Length >= 5 && ntfyInput.Length <= 64)
        {
            presetToConfigure.NtfyTopic = ntfyInput.Trim();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid topic ID length. Press any key to try again.");
            Console.ResetColor();
            Console.ReadKey(true);
            EditNtfyTopic(presetToConfigure); // Retry
        }
    }

    private static void EditPriceRiseThreshold(PresetConfig presetToConfigure)
    {
        Console.Write("Enter price rise threshold (e.g., 8.50) or press Enter to disable: ");
        string priceRiseInput = ReadLineWithCancel().Replace(',', '.');

        if (string.IsNullOrEmpty(priceRiseInput))
        {
            presetToConfigure.PriceRiseThreshold = 0.0f;
        }
        else if (float.TryParse(priceRiseInput, NumberStyles.Any, CultureInfo.InvariantCulture, out float riseThreshold) && riseThreshold > 0)
        {
            presetToConfigure.PriceRiseThreshold = riseThreshold;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid input. Enter a positive number. Press any key to try again.");
            Console.ResetColor();
            Console.ReadKey(true);
            EditPriceRiseThreshold(presetToConfigure); // Retry
        }
    }

    private static void EditPriceDropThreshold(PresetConfig presetToConfigure)
    {
        Console.Write("Enter price drop threshold (e.g., 5.23) or press Enter to disable: ");
        string priceDropInput = ReadLineWithCancel().Replace(',', '.');

        if (string.IsNullOrEmpty(priceDropInput))
        {
            presetToConfigure.PriceDropThreshold = 0.0f;
        }
        else if (float.TryParse(priceDropInput, NumberStyles.Any, CultureInfo.InvariantCulture, out float dropThreshold) && dropThreshold > 0)
        {
            presetToConfigure.PriceDropThreshold = dropThreshold;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid input. Enter a positive number. Press any key to try again.");
            Console.ResetColor();
            Console.ReadKey(true);
            EditPriceDropThreshold(presetToConfigure); // Retry
        }
    }

    private static string ReadLineWithCancel()
    {
        StringBuilder inputBuffer = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo keyInfo = Console.ReadKey(intercept: true);

            if (keyInfo.Key == ConsoleKey.Escape)
            {
                throw new OperationCanceledException("Input cancelled by user.");
            }

            if (keyInfo.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return inputBuffer.ToString();
            }

            if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (inputBuffer.Length > 0)
                {
                    inputBuffer.Remove(inputBuffer.Length - 1, 1);
                    // Visual backspace handling
                    Console.Write("\b \b");
                }
            }
            else if (!char.IsControl(keyInfo.KeyChar))
            {
                inputBuffer.Append(keyInfo.KeyChar);
                Console.Write(keyInfo.KeyChar);
            }
        }
    }


    // === Fetch price data from Steam Market API and display it ===
    private static async Task FetchAndDisplayPrice()
    {
        Console.Clear();
        Console.WriteLine($"--- Last update: {DateTime.Now:HH:mm:ss} ---");
        Console.WriteLine($"Tracking item: {itemName.Replace("%20", " ").Replace("%7C", "|").Replace("%E2%98%85", "★").Replace("%E2%84%A2", "™").Replace("%28", "(").Replace("%29", ")")}");

        #if DEBUG
            Console.WriteLine($"{apiUrl}");
        #endif

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            var priceData = JsonSerializer.Deserialize(responseBody, SourceGenerationContext.Default.SteamMarketPrice);

            if (priceData?.Success == true && priceData.LowestPrice != null && priceData.MedianPrice != null)
            {
                await LogPriceUpdate(priceData);
                float currentLowest = ParsePrice(priceData.LowestPrice);
                float currentMedian = ParsePrice(priceData.MedianPrice);

                Console.WriteLine("------------------------------------------");
                PrintPriceWithColor("Lowest price: \t\t", priceData.LowestPrice, currentLowest, previousLowestPrice);
                PrintPriceWithColor("Median price: \t\t", priceData.MedianPrice, currentMedian, previousMedianPrice);
                Console.WriteLine($"Volume (24h): \t\t{priceData.Volume}");
                Console.WriteLine("------------------------------------------");
                Console.WriteLine($"Lowest price change: \t{(previousLowestPrice.HasValue ? (currentLowest - previousLowestPrice.Value).ToString("F2", CultureInfo.InvariantCulture) : "N/A")}");
                Console.WriteLine($"Median price change: \t{(previousMedianPrice.HasValue ? (currentMedian - previousMedianPrice.Value).ToString("F2", CultureInfo.InvariantCulture) : "N/A")}");
                Console.WriteLine("------------------------------------------");

                if (currentLowest > priceRiseThreshold && isNtfyEnabled && priceRiseThreshold != 0.0f && !isAlertSent)
                {
                    Console.WriteLine("\nWent over the threshold! Sending notification...");
                    string message = $"Item's price went over the threshold! Current price: {priceData.LowestPrice}";
                    await SendNtfyNotification(message);
                    isAlertSent = true; // Set alert as sent
                }
                // Reset alert if price is not hitting either threshold
                else if (currentLowest <= priceRiseThreshold && currentLowest >= priceDropThreshold && isAlertSent)
                {
                    Console.WriteLine("\nPrice not hitting the threshold anymore. Alert reset.");
                    isAlertSent = false;
                }

                if (currentLowest < priceDropThreshold && isNtfyEnabled && priceDropThreshold != 0.0f && !isAlertSent)
                {
                    Console.WriteLine("\nPrice is below the drop threshold. Sending notification...");
                    string message = $"Item's price is below the price drop threshold! Current price: {priceData.LowestPrice}";
                    await SendNtfyNotification(message);
                    isAlertSent = true;
                }

                // Update previous prices for next comparison
                previousLowestPrice = currentLowest;
                previousMedianPrice = currentMedian;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("API did respond with a message, but with incorrect data. Check if item's name is set correctly");
                Console.ResetColor();
            }
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"\nAn error occured: {e.Message}");
            Console.ResetColor();
        }
    }

    private static async Task LogPriceUpdate(SteamMarketPrice priceData)
    {
        if (_tempLogFilePath == null || priceData.LowestPrice == null || priceData.MedianPrice == null || priceData.Volume == null)
        {
            return;
        }

        try
        {
            string timestamp = DateTime.UtcNow.ToString("o");
            string lowestPrice = ParsePrice(priceData.LowestPrice).ToString(CultureInfo.InvariantCulture);
            string medianPrice = ParsePrice(priceData.MedianPrice).ToString(CultureInfo.InvariantCulture);
            string volume = priceData.Volume.Replace(",", "");

            string csvLine = $"{timestamp},{lowestPrice},{medianPrice},{volume}\n";

            await File.AppendAllTextAsync(_tempLogFilePath, csvLine);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"Failed to write to log file: {ex.Message}");
            Console.ResetColor();
        }
    }

    private static void ExportDataToCsv()
    {
        if (_tempLogFilePath == null || !File.Exists(_tempLogFilePath))
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("No log data to export.");
            Console.ResetColor();
            return;
        }

        try
        {
            string sanitizedItemName = itemName.Replace("%20", "").Replace("%7C", "").Replace("%E2%98%85", "").Replace("%E2%84%A2", "").Replace("%28", "(").Replace("%29", ")");
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"MarketLog_{sanitizedItemName}_{timestamp}.csv";
            string finalPath = Path.Combine(AppContext.BaseDirectory, fileName);

            File.Copy(_tempLogFilePath, finalPath, true);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nData exported successfully to: {fileName}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"\nFailed to export data: {ex.Message}");
            Console.ResetColor();
        }
    }


    // === Send notification using ntfy.sh service ===
    private static async Task SendNtfyNotification(string message)
    {
        try
        {
            var content = new StringContent(message, Encoding.UTF8, "text/plain");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://ntfy.sh/{ntfyTopic}")
            {
                Content = content
            };
            request.Headers.Add("Title", "Steam Market Notifier");
            request.Headers.Add("Priority", "high");
            request.Headers.Add("Tags", "tada");

            using var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("Notification sent successfuly!");
            }
            else
            {
                Console.WriteLine($"Error while sending the notification: {response.StatusCode}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Critical error while sending the notification: {e.Message}");
        }
    }

    // === Supporting function to parse price strings ===
    private static float ParsePrice(string priceString)
    {
        string cleanPrice = System.Text.RegularExpressions.Regex.Replace(priceString, @"[^\d,.]", "").Replace(",", ".");
        if (float.TryParse(cleanPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out float result))
        {
            return result;
        }
        return 0;
    }

    // === Supporting function to print prices with color based on change ===
    private static void PrintPriceWithColor(string label, string priceString, float currentPrice, float? previousPrice)
    {
        Console.Write(label);

        if (previousPrice.HasValue)
        {
            if (currentPrice > previousPrice.Value)
            {
                Console.ForegroundColor = ConsoleColor.Green;
            }
            else if (currentPrice < previousPrice.Value)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }
        }

        // If previousPrice is null, just print the price without color
        Console.WriteLine(priceString);
        Console.ResetColor();
    }

    private static RootConfig? LoadConfig()
    {
        if (File.Exists(configFile))
        {
            try
            {
                string json = File.ReadAllText(configFile);
                return JsonSerializer.Deserialize(json, SourceGenerationContext.Default.RootConfig);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading config file: {ex.Message}");
                return null;
            }
        }
        return null;
    }

    private static void SaveConfig(RootConfig config)
    {
        try
        {
            string json = JsonSerializer.Serialize(config, SourceGenerationContext.Default.RootConfig);
            File.WriteAllText(configFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving config file: {ex.Message}");
        }
    }
}