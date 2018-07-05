using System;
using System.Windows;
using System.IO;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Configuration;
using System.Net;
using System.Security.Cryptography;
using Newtonsoft.Json;
using System.Threading;
using ZXing.QrCode;
using ZXing;
using ZXing.Common;
using System.Windows.Media.Imaging;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Windows.Navigation;
using HTMLConverter;
using System.Windows.Markup;
using System.Windows.Documents;
using System.Linq;
using System.Text;

namespace DNotesInvoicePOC
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const long DNotesToSatoshi = 100000000;
        private const string salt = "hcH3Cm3gPn3O2zQLqzjrnQ==";
        private const string key = "SdrkxNdzwj7TMqwY";

        private string StateFile = Directory.GetCurrentDirectory() + "\\subscription.json";

        private enum SubscriptionType
        {
            day = 0,
            week = 1,
            unlimited = 2
        }
        
        public MainWindow()
        {
            InitializeComponent();
            var state = ParseState();

            if (CheckSubscription())
            {
                EnableSoftware();
            }
            //subscription is not verified, but waiting for payment
            else if (state != null)
            {
                ShowPaymentScreen();
                LoopCheck();
            }
        }

        private void EnableSoftware()
        {
            opt1.Visibility = Visibility.Hidden;
            opt2.Visibility = Visibility.Hidden;
            opt3.Visibility = Visibility.Hidden;
            subLabel.Visibility = Visibility.Hidden;
            paidLabel.Visibility = Visibility.Visible;
            verifyLabel.Visibility = Visibility.Hidden;
            qrCodeImage.Visibility = Visibility.Hidden;
        }

        private bool SoftwareEnabled()
        {
            return paidLabel.Visibility == Visibility.Visible;
        }

        private void ShowPaymentScreen()
        {
            opt1.Visibility = Visibility.Hidden;
            opt2.Visibility = Visibility.Hidden;
            opt3.Visibility = Visibility.Hidden;
            subLabel.Visibility = Visibility.Hidden;
            paidLabel.Visibility = Visibility.Hidden;
            verifyLabel.Visibility = Visibility.Visible;
            qrCodeImage.Visibility = Visibility.Visible;
            GenerateQRCodeAndLink(ParseState());
        }
        private void InitialScreen()
        {
            opt1.Visibility = Visibility.Visible;
            opt2.Visibility = Visibility.Visible;
            opt3.Visibility = Visibility.Visible;
            subLabel.Visibility = Visibility.Visible;
            paidLabel.Visibility = Visibility.Hidden;
            verifyLabel.Visibility = Visibility.Hidden;
            qrCodeImage.Visibility = Visibility.Hidden;
        }

        private void GenerateQRCodeAndLink(dynamic state)
        {
            
            var qrcode = new QRCodeWriter();
            var price = (double)state.price / DNotesToSatoshi;
            var qrValue = string.Format("dnotes:{0}?amount={1}&invoice={2}", ConfigurationManager.AppSettings["address"], price, state.invoice);

            var payText = string.Format("Please <a href='{0}'>pay</a> {1} NOTE to {2} for invoice {3}.\nWhen payment is verified, software will become active.",
                qrValue, price, ConfigurationManager.AppSettings["address"], state.invoice);

            var xaml = HtmlToXamlConverter.ConvertHtmlToXaml(payText, true);
            var flowDocument = XamlReader.Parse(xaml);
            HyperlinkEvents(flowDocument);
            verifyLabel.Document = flowDocument;

            var barcodeWriter = new BarcodeWriter
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new EncodingOptions
                {
                    Height = 300,
                    Width = 300,
                    Margin = 1
                }
            };

            using (var bitmap = barcodeWriter.Write(qrValue))
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);

                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                stream.Seek(0, SeekOrigin.Begin);
                bi.StreamSource = stream;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                qrCodeImage.Source = bi; 
            }
        }

        private static void HyperlinkEvents(FlowDocument flowDocument)
        {
            if (flowDocument == null) return;
            GetVisualChildren(flowDocument).OfType<Hyperlink>().ToList()
                     .ForEach(i => i.RequestNavigate += HyperlinkNavigate);
        }

        private static IEnumerable<DependencyObject> GetVisualChildren(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                yield return child;
                foreach (var descendants in GetVisualChildren(child)) yield return descendants;
            }
        }

        private static void HyperlinkNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="t"></param>
        private void CreateSubscription(SubscriptionType t)
        {
            //generate a random invoice #
            var sha256 = SHA256Managed.Create();
            var guidBytes = Guid.NewGuid().ToByteArray();
            var invoice = Convert.ToBase64String(sha256.ComputeHash(guidBytes)).Replace("+","").Replace("=","").Replace(@"/", "").Substring(0,20);

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expiration = DateTimeOffset.MaxValue.ToUnixTimeSeconds();
            var reqConfirmations = int.Parse(ConfigurationManager.AppSettings["reqConfirmations"]);

            double priceUSD = 0;
            switch (t)
            {
                case SubscriptionType.day:
                    priceUSD = 0.99;
                    //TODO: return to normal
                    //expiration = now + 86400 + (reqConfirmations * 60);
                    expiration = now + 60 * 5;
                    break;
                case SubscriptionType.week:
                    priceUSD = 4.99;
                    expiration = now + 604800 + (reqConfirmations * 60);
                    break;
                default:
                    priceUSD = 29.99;
                    break;
            }

            var priceNOTE = USDtoDNotes(priceUSD);
            var state = string.Format("{{'subtype' : '{0}', 'invoice': '{1}', 'price': '{2}', 'expiration': '{3}'}}", (int)t, invoice, priceNOTE, expiration);

            File.WriteAllText(StateFile, state);
            EncryptFile(StateFile);

            ShowPaymentScreen();
            LoopCheck();
        }

        /// <summary>
        /// Is the subscription paid?
        /// </summary>
        /// <returns></returns>
        private bool CheckSubscription()
        {
            if (!File.Exists(StateFile))
            {
                return false;
            }

            var json = ParseState();

            var invoice = json.invoice;
            var price = long.Parse((string) json.price);

            if (!CheckPayment((string) invoice, price, ConfigurationManager.AppSettings["address"], int.Parse(ConfigurationManager.AppSettings["reqConfirmations"])))
            {
                return false;
            }

            if (json.ContainsKey("expiration"))
            {
                var expiration = long.Parse((string) json.expiration);
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (now >= expiration)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Verify payement on the API
        /// </summary>
        /// <param name="invoice">Invoice to be paid</param>
        /// <param name="price">Amount of the invoice</param>
        /// <param name="address">Address the invoice should be paid to</param>
        /// <param name="reqConfirmations">Number of confirmations required for payment</param>
        /// <returns></returns>
        private bool CheckPayment(string invoice, long price, string address, int reqConfirmations)
        {
            String[] respArr;
            double amount = 0;
            int confirmations = 0;

            string url = "";

            if (bool.Parse(ConfigurationManager.AppSettings["testnet"]))
            {
                url = @"http://dnotesdevlinux4.southcentralus.cloudapp.azure.com/chain/DNotesTestnet/q/invoice/" + address + '+' + invoice;
            }
            else
            {
                url = @"https://abe.dnotescoin.com/chain/DNotes/q/invoice/" + address + '+' + invoice;
            }

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                respArr = reader.ReadToEnd().Split(',');
                amount = double.Parse(respArr[0]);
                confirmations = int.Parse(respArr[1]);
            }

            if (confirmations >= reqConfirmations && (amount * DNotesToSatoshi >= price))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Use coin market cap to get conversion
        /// </summary>
        /// <param name="USD"></param>
        /// <returns></returns>
        private long USDtoDNotes(double priceUSD)
        {
            double USDPerDNote = 0.0;            

            string url = @"https://api.coinmarketcap.com/v2/ticker/184/";

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                dynamic respJson = JsonConvert.DeserializeObject(reader.ReadToEnd());
                USDPerDNote = (double) respJson.data.quotes.USD.price;                
            }

            return (long) ((priceUSD / USDPerDNote) * DNotesToSatoshi);
        }

        private void LoopCheck()
        {
            new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;
                while (true)
                {
                    Thread.Sleep(5000);
                    if (CheckSubscription())
                    {
                        this.Dispatcher.Invoke(() =>
                        {
                            EnableSoftware();
                        });
                    }
                    else if (File.Exists(StateFile) && !SoftwareEnabled())
                    {
                        this.Dispatcher.Invoke(() =>
                        {
                            ShowPaymentScreen();
                        });
                    }
                    else
                    {
                        //license expired, delete subscription and show the initial screen
                        this.Dispatcher.Invoke(() =>
                        {
                            InitialScreen();
                            File.Delete(StateFile);
                        });                        
                    }
                }                
            }).Start();
        }

        private dynamic ParseState()
        {
            if (!File.Exists(StateFile))
            {
                return null;
            }
            var subFileCopy = Directory.GetCurrentDirectory() + "\\subCopy.json";

            if (File.Exists(subFileCopy))
            {
                File.Delete(subFileCopy);
            }

            File.Copy(StateFile, subFileCopy);
            //File.Decrypt(subFileCopy);
            DecryptFile(subFileCopy);
            dynamic json = JsonConvert.DeserializeObject(File.ReadAllText(subFileCopy));
            File.Delete(subFileCopy);
            return json;
        }

        private void Subscribe1Day(object sender, RoutedEventArgs e)
        {
            CreateSubscription(SubscriptionType.day);
        }

        private void Subscribe7Days(object sender, RoutedEventArgs e)
        {
            CreateSubscription(SubscriptionType.week);
        }

        private void SubscribeUnlimited(object sender, RoutedEventArgs e)
        {
            CreateSubscription(SubscriptionType.unlimited);
        }

        private void EncryptFile(string path)
        {
            var content = File.ReadAllText(path);

            var ivBytes = Convert.FromBase64String(salt);
            var keyBytes = new ASCIIEncoding().GetBytes(key);

            byte[] toEncrypt = new ASCIIEncoding().GetBytes(Convert.ToBase64String(new ASCIIEncoding().GetBytes(content)));

            MemoryStream memStream = new MemoryStream();

            CryptoStream cStream = new CryptoStream(memStream, new TripleDESCryptoServiceProvider().CreateEncryptor(keyBytes, ivBytes), CryptoStreamMode.Write);

            cStream.Write(toEncrypt, 0, toEncrypt.Length);
            cStream.FlushFinalBlock();

            byte[] result = memStream.ToArray();

            cStream.Close();
            memStream.Close();
            var encryptedContent = Convert.ToBase64String(result);
            File.WriteAllText(path, encryptedContent);
        }

        private void DecryptFile(string path)
        {
            var content = File.ReadAllText(path);

            var ivBytes = Convert.FromBase64String(salt);
            var keyBytes = new ASCIIEncoding().GetBytes(key);

            var toDecryptArray = Convert.FromBase64String(content);

            MemoryStream memStream = new MemoryStream(toDecryptArray);

            CryptoStream cStream = new CryptoStream(memStream, new TripleDESCryptoServiceProvider().CreateDecryptor(keyBytes, ivBytes), CryptoStreamMode.Read);

            byte[] res = new byte[toDecryptArray.Length];

            cStream.Read(res, 0, res.Length);

            var decryptedContent = new ASCIIEncoding().GetString(Convert.FromBase64String(new ASCIIEncoding().GetString(res).TrimEnd('\0')));
            File.WriteAllText(path, decryptedContent);
        }
    }
}
