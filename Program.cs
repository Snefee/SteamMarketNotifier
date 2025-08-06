using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;


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

public class Program
{
    private static readonly HttpClient client = new HttpClient();

    private static int currencyType = 1; // Default currency = USD
    private static string ApiUrl => $"https://steamcommunity.com/market/priceoverview/?currency={currencyType}&appid=730&market_hash_name=Stockholm%202021%20Legends%20Sticker%20Capsule";

    private static float PriceThreshold;
    private const string NtfyTopic = "x";

    // Last prices variables to track changes
    private static float? previousLowestPrice = null;
    private static float? previousMedianPrice = null;

    private static bool isAlertSent = false; // Prevents multiple alerts for the same price threshold


    // === Main method to configure all settings ===
    public static async Task Main(string[] args)
    {
        Console.WriteLine("--- Configure Price Alert ---");
        while (true)
        {
            Console.Write("Input price threshold at which the alert is sent (ex. 08,50): ");
            string? priceInput = Console.ReadLine();

            // Convert comma to dot for decimal parsing
            if (priceInput != null)
            {
                priceInput = priceInput.Replace(',', '.');
            }

            if (float.TryParse(priceInput, NumberStyles.Any, CultureInfo.InvariantCulture, out PriceThreshold) && PriceThreshold > 0)
            {
                break;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Wrong input. Input positive number.");
                Console.ResetColor();
            }
        }

        Console.WriteLine("--- Choose your preffered currency ---");
        while (true)
        {
            Console.WriteLine("Input currency type (1-10): ");
            Console.WriteLine("1. USD (default)");
            Console.WriteLine("2. GBP");
            Console.WriteLine("3. EUR");
            Console.WriteLine("4. CHF");
            Console.WriteLine("5. RUB");
            Console.WriteLine("6. PLN");
            Console.WriteLine("7. BRL");
            Console.WriteLine("8. JPY");
            Console.WriteLine("9. NOK");
            Console.WriteLine("10. AED");

            string? currencyInput = Console.ReadLine();

            if (int.TryParse(currencyInput, out currencyType) && currencyType >= 1 && currencyType <= 10)
            {
                break;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Wrong input. Input a number between 1 to 10.");
                Console.ResetColor();
            }
        }

        Console.WriteLine("Tracking Steam Market price of selected item...");
        while (true)
        {
            await FetchAndDisplayPrice();
            Console.WriteLine($"\nNext update in 5 minutes...");
            await Task.Delay(TimeSpan.FromMinutes(5));
        }
    }

    // === Fetch price data from Steam Market API and display it ===
    private static async Task FetchAndDisplayPrice()
    {
        Console.Clear();
        Console.WriteLine($"--- Last update: {DateTime.Now:HH:mm:ss} ---");
        Console.WriteLine($"{ApiUrl}");

        try
        {
            string responseBody = await client.GetStringAsync(ApiUrl);
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

                if (currentLowest > PriceThreshold && !isAlertSent)
                {
                    Console.WriteLine("\nWent over the threshold! Sending notification...");
                    string message = $"Item's price went over the threshold! Current price: {priceData.LowestPrice}";
                    await SendNtfyNotification(message);
                    isAlertSent = true; // Set alert as sent
                }
                // Reset alert if price falls below threshold
                else if (currentLowest <= PriceThreshold && isAlertSent)
                {
                    Console.WriteLine("\nPrice fell below the threshold. Alert reset.");
                    isAlertSent = false;
                }

                // Update previous prices for next comparison
                previousLowestPrice = currentLowest;
                previousMedianPrice = currentMedian;
            }
            else
            {
                Console.WriteLine("API did respond with a message, but with incorrect data");
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
            var request = new HttpRequestMessage(HttpMethod.Post, $"https://ntfy.sh/{NtfyTopic}")
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
}