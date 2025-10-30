using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.IO;
using System.Data;

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
    private static readonly string appVersion = "0.8.1";

    private static readonly string configFile = Path.Combine(AppContext.BaseDirectory, "config.json");


    public static async Task Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
    MainMenu:
        // Load configuration from file
        var config = LoadConfig();
        if (config != null)
            while (true)
            {
                Console.Clear();

                // Use the 'config' object that was loaded once.
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
                Console.WriteLine($"Tracked Item: {itemName.Replace("%20", " ").Replace("%7C", "|").Replace("%E2%98%85", "★")}");
                Console.WriteLine($"Currency Type: {currencyType}");
                Console.WriteLine($"Ntfy Topic: {ntfyTopic}");
                Console.WriteLine($"Price Rise Threshold: {priceRiseThreshold}");
                Console.WriteLine($"Price Drop Threshold: {priceDropThreshold}");
                Console.WriteLine("-----------------------------------");

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\nUse Left/Right arrow keys [or A/D] to switch presets.");
                Console.WriteLine("Press C to change selected preset's configuration");
                Console.WriteLine("Press Enter to start tracking.");
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
                    Console.Clear();
                    Console.WriteLine($"--- Configuring Preset {activePreset} ---");

                    var presetToEdit = config.Presets[activePreset - 1];
                    ConfigurePresetDetails(presetToEdit);
                    SaveConfig(config);
                }
                else if (keyInfo.Key == ConsoleKey.Enter)
                {
                    // Save the last selected preset before starting
                    config.ActivePreset = activePreset;
                    SaveConfig(config);
                    break;
                }
            }
        else
        {
            // First time setup
            Console.WriteLine("--- Welcome to Steam Market Notifier! ---");
            
            var newRootConfig = new RootConfig { ActivePreset = 1 };
            for (int i = 0; i < 5; i++)
            {
                newRootConfig.Presets.Add(new PresetConfig { ItemName = "Not Set" });
            }

            var firstPreset = newRootConfig.Presets[0];
            ConfigurePresetDetails(firstPreset);
            
            SaveConfig(newRootConfig);

            Console.WriteLine("Configuration saved!");
            await Task.Delay(1000);
            goto MainMenu;
        }

        while (true)
        {
            await FetchAndDisplayPrice();
            Console.WriteLine($"\nNext update in 5 minutes...\nPress ESC to return to menu");

            for (int i = 0; i < 3000; i++)
            {
                if (Console.KeyAvailable)
                {
                    var keyInfo = Console.ReadKey(true);
                    if (keyInfo.Key == ConsoleKey.Escape)
                    {
                        goto MainMenu;
                    }
                }
                await Task.Delay(100);
            }
        }
    }

    private static void ConfigurePresetDetails(PresetConfig presetToConfigure)
    {
        // Configure Item Name
        while (true)
        {
            Console.WriteLine("Write the exact english name of the item you want to track (case-sensitive):");
            string? itemInput = Console.ReadLine();

            if (!string.IsNullOrEmpty(itemInput))
            {
                presetToConfigure.ItemName = itemInput.Replace(" ", "%20").Replace("|", "%7C").Replace("★", "%E2%98%85");
                break;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Item name cannot be empty. Please try again.");
                Console.ResetColor();
            }
        }

        // Configure Currency
        while (true)
        {
            Console.WriteLine("--- Choose your preffered currency ---");
            Console.WriteLine("Input currency type (1-10): ");
            Console.WriteLine("1. USD (default)\n2. GBP\n3. EUR\n4. CHF\n5. RUB\n6. PLN\n7. BRL\n8. JPY\n9. NOK\n10. AED");

            string? currencyInput = Console.ReadLine();
            if (int.TryParse(currencyInput, out int selectedCurrency) && selectedCurrency >= 1 && selectedCurrency <= 10)
            {
                presetToConfigure.CurrencyType = selectedCurrency;
                break;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Wrong input. Input a number between 1 to 10.");
                Console.ResetColor();
            }
        }

        // Configure Ntfy.sh topic
        while (true)
        {
            Console.WriteLine("--- Set your Ntfy topic ID for notifications ---");
            Console.WriteLine("If you don't have one, you check how to make it here: https://github.com/Snefee/SteamMarketNotifier/wiki/NTFY-Setup");
            Console.WriteLine("Or leave it empty and press enter to skip notifications.");

            string? ntfyInput = Console.ReadLine();
            if (string.IsNullOrEmpty(ntfyInput))
            {
                presetToConfigure.NtfyTopic = string.Empty;
                break;
            }
            else if (ntfyInput.Length >= 5 && ntfyInput.Length <= 64)
            {
                presetToConfigure.NtfyTopic = ntfyInput.Trim();
                break;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Wrong input. Input a valid token ID (5-64 characters).");
                Console.ResetColor();
            }
        }

        // Configure Price Alerts if Ntfy is enabled
        if (!string.IsNullOrEmpty(presetToConfigure.NtfyTopic))
        {
            Console.WriteLine("--- Configure Price Alert ---");
            while (true)
            {
                Console.Write("Enter price rise threshold (ex. 08,50) or press enter to skip: ");
                string? priceRiseInput = Console.ReadLine()?.Replace(',', '.');

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
                    Console.WriteLine("Wrong input. Enter a positive number.");
                    Console.ResetColor();
                    continue;
                }

                Console.Write("Enter price drop threshold (ex. 05,23) or press enter to skip: ");
                string? priceDropInput = Console.ReadLine()?.Replace(',', '.');

                if (string.IsNullOrEmpty(priceDropInput))
                {
                    presetToConfigure.PriceDropThreshold = 0.0f;
                    break;
                }
                else if (float.TryParse(priceDropInput, NumberStyles.Any, CultureInfo.InvariantCulture, out float dropThreshold) && dropThreshold > 0)
                {
                    presetToConfigure.PriceDropThreshold = dropThreshold;
                    break;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Wrong input. Enter a positive number.");
                    Console.ResetColor();
                }
            }
        }
    }


    // === Fetch price data from Steam Market API and display it ===
    private static async Task FetchAndDisplayPrice()
    {
        Console.Clear();
        Console.WriteLine($"--- Last update: {DateTime.Now:HH:mm:ss} ---");
        Console.WriteLine($"Tracking item: {itemName.Replace("%20", " ").Replace("%7C", "|").Replace("%E2%98%85", "★")}");
        Console.WriteLine($"{apiUrl}");

        try
        {
            string responseBody = await client.GetStringAsync(apiUrl);
            var priceData = JsonSerializer.Deserialize<SteamMarketPrice>(responseBody);

            if (priceData?.Success == true && priceData.LowestPrice != null && priceData.MedianPrice != null)
            {
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

    // === Send notification using ntfy.sh service ===
    private static async Task SendNtfyNotification(string message)
    {
        try
        {
            var content = new StringContent(message, Encoding.UTF8, "text/plain");
            var request = new HttpRequestMessage(HttpMethod.Post, $"https://ntfy.sh/{ntfyTopic}")
            {
                Content = content
            };
            request.Headers.Add("Title", "Steam Market Notifier");
            request.Headers.Add("Priority", "high");
            request.Headers.Add("Tags", "tada");

            var response = await client.SendAsync(request);
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
                return JsonSerializer.Deserialize<RootConfig>(json);
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
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving config file: {ex.Message}");
        }
    }
}