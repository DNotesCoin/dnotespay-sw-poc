using System;
using System.Windows;
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using Newtonsoft.Json;
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
using System.Windows.Threading;

namespace DNotesInvoicePOC
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DispatcherTimer _timer;

        private const long DNotesToSatoshi = 100000000;

        /*TODO: generate your own random salt and key for encryption
         * The key is 16 random letters and numbers (case sensitive), the salt is a base64 encoding of
         a random array of 16 bytes generated with the following code:
            var rngCsp = new RNGCryptoServiceProvider();
            byte[] ivBytes = new byte[16];
            rngCsp.GetBytes(ivBytes);
        */
        private const string Salt = "hcH3Cm3gPn3O2zQLqzjrnQ==";
        private const string Key = "SdrkxNdzwj7TMqwY";

        /*TODO: testing parameters */
        private const bool Testnet = true;
        private const string TestnetAddress = "TXFjPSgevKLk1n7Z9TB2uRxDZTDaBjveC1";
        
        /*TODO: number of confirmations you would like to see in the blockchain 
         * before payment is considered confirmed.*/
        private const int ReqConfirmations = 0;

        private const string CopyAmtUrl = "copyamt";
        private const string CopyAddrUrl = "copyaddr";

        private string StateFile = Directory.GetCurrentDirectory() + "\\subscription.json";

        private enum SubscriptionType
        {
            Day = 0,
            Week = 1,
            Month = 2,
            Year = 3
        }
        
        public MainWindow()
        {
            InitializeComponent();
            var state = ParseState();

            this._timer = new DispatcherTimer();
            this._timer.Interval = new TimeSpan(0, 0, 10);

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
            else
            {
                InitialScreen();
            }

        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (CheckSubscription())
            {
                EnableSoftware();
            }
            else if (SoftwareEnabled())
            {
                File.Delete(StateFile);
                InitialScreen();
            }
        }
        
        private void EnableSoftware()
        {
            opt1.Visibility = Visibility.Hidden;
            opt2.Visibility = Visibility.Hidden;
            opt3.Visibility = Visibility.Hidden;
            opt4.Visibility = Visibility.Hidden;
            subLabel.Visibility = Visibility.Hidden;
            paidLabel.Visibility = Visibility.Visible;
            payLabel.Visibility = Visibility.Hidden;
            qrCodeImage.Visibility = Visibility.Hidden;
            progressImage.Visibility = Visibility.Hidden;
            progressLabel.Visibility = Visibility.Hidden;
            payElectrumButton.Visibility = Visibility.Hidden;
            usdLabel.Visibility = Visibility.Hidden;
            payDNotesLabel.Visibility = Visibility.Hidden;
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
            opt4.Visibility = Visibility.Hidden;
            subLabel.Visibility = Visibility.Hidden;
            paidLabel.Visibility = Visibility.Hidden;
            payLabel.Visibility = Visibility.Visible;
            qrCodeImage.Visibility = Visibility.Visible;
            progressImage.Visibility = Visibility.Visible;
            progressLabel.Visibility = Visibility.Visible;
            payElectrumButton.Visibility = Visibility.Visible;
            usdLabel.Visibility = Visibility.Visible;
            payDNotesLabel.Visibility = Visibility.Visible;
            GenerateQRCodeAndLink(ParseState());
        }
        private void InitialScreen()
        {
            opt1.Visibility = Visibility.Visible;
            opt2.Visibility = Visibility.Visible;
            opt3.Visibility = Visibility.Visible;
            opt4.Visibility = Visibility.Visible;
            subLabel.Visibility = Visibility.Visible;
            paidLabel.Visibility = Visibility.Hidden;
            payLabel.Visibility = Visibility.Hidden;
            qrCodeImage.Visibility = Visibility.Hidden;
            progressImage.Visibility = Visibility.Hidden;
            progressLabel.Visibility = Visibility.Hidden;
            payElectrumButton.Visibility = Visibility.Hidden;
            usdLabel.Visibility = Visibility.Hidden;
            payDNotesLabel.Visibility = Visibility.Hidden;
        }

        private void GenerateQRCodeAndLink(dynamic state)
        {
            
            var qrcode = new QRCodeWriter();
            var price = (decimal)state.price / DNotesToSatoshi;
            var address = (string)state.address;
            var qrValue = string.Format("dnotes:{0}?amount={1}&invoice={2}", address, price, state.invoice);
            var priceUSD = (decimal)state.priceUSD;
            var invoice = (string)state.invoice;

            var payText = string.Format("<strong>Please send exactly:</strong> {0:0.00000000} NOTE <a href='" + CopyAmtUrl + "'>copy</a><br/>" +
                                        "<strong>To:</strong> {1}+{2} <a href='" + CopyAddrUrl + "'>copy</a>",
                                        price, address, invoice);

            usdLabel.Content = string.Format("USD: ${0:0.00}", priceUSD);

            var xaml = HtmlToXamlConverter.ConvertHtmlToXaml(payText, true);
            dynamic flowDocument = XamlReader.Parse(xaml);
            HyperlinkEvents(flowDocument);
            payLabel.Document = flowDocument;

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

        private void HyperlinkEvents(FlowDocument flowDocument)
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

        private void HyperlinkNavigate(object sender, RequestNavigateEventArgs e)
        {
            var state = ParseState();
            var uri = e.Uri.OriginalString;
            if (uri == CopyAmtUrl)
            {
                Clipboard.SetText((string)state.price);
            }
            else if (uri == CopyAddrUrl)
            {
                Clipboard.SetText((string)state.address + "+" + (string)state.invoice);
            }
            else
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            }
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
            var reqConfirmations = ReqConfirmations;

            double priceUSD = 0;
            switch (t)
            {

                /*TODO: add your own subscription options*/
                case SubscriptionType.Day:
                    priceUSD = 2;
                    expiration = now + 86400 + (reqConfirmations * 60);
                    break;
                case SubscriptionType.Week:
                    priceUSD = 10;
                    expiration = now + 604800 + (reqConfirmations * 60);
                    break;
                case SubscriptionType.Month:
                    priceUSD = 30;
                    expiration = now + 2592000 + (reqConfirmations * 60);
                    break;
                default:
                    priceUSD = 275;
                    expiration = now + 31536000 + (reqConfirmations * 60);
                    break;
            }

            var priceNOTE = USDtoDNotes(priceUSD);
            var address = Testnet ? TestnetAddress : MainnetAddress();
            var state = string.Format("{{'subtype' : '{0}', 'invoice': '{1}', 'price': '{2}', 'expiration': '{3}', 'address': '{4}', 'priceUSD': '{5}'}}", (int)t, invoice, priceNOTE, expiration, address, priceUSD);

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

            if (!CheckPayment((string) invoice, price, (string)json.address, ReqConfirmations))
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
        /// <param name="address">address the invoice should be paid to</param>
        /// <param name="reqConfirmations">Number of confirmations required for payment</param>
        /// <returns></returns>
        private bool CheckPayment(string invoice, long price, string address, int reqConfirmations)
        {
            String[] respArr;
            double amount = 0;
            int confirmations = 0;

            string url = "";

            if (Testnet)
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
            if (!this._timer.IsEnabled)
            {
                this._timer.Start();
            }
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
            DecryptFile(subFileCopy);
            dynamic json = JsonConvert.DeserializeObject(File.ReadAllText(subFileCopy));
            File.Delete(subFileCopy);
            return json;
        }

        private void Subscribe1Day_Click(object sender, RoutedEventArgs e)
        {
            CreateSubscription(SubscriptionType.Day);
        }

        private void Subscribe7Days_Click(object sender, RoutedEventArgs e)
        {
            CreateSubscription(SubscriptionType.Week);
        }

        private void Subscribe30Days_Click(object sender, RoutedEventArgs e)
        {
            CreateSubscription(SubscriptionType.Month);
        }

        private void Subscribe1Year_Click(object sender, RoutedEventArgs e)
        {
            CreateSubscription(SubscriptionType.Year);
        }

        private void EncryptFile(string path)
        {
            var content = File.ReadAllText(path);

            var ivBytes = Convert.FromBase64String(Salt);
            var keyBytes = new ASCIIEncoding().GetBytes(Key);

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

            var ivBytes = Convert.FromBase64String(Salt);
            var keyBytes = new ASCIIEncoding().GetBytes(Key);

            var toDecryptArray = Convert.FromBase64String(content);

            MemoryStream memStream = new MemoryStream(toDecryptArray);

            CryptoStream cStream = new CryptoStream(memStream, new TripleDESCryptoServiceProvider().CreateDecryptor(keyBytes, ivBytes), CryptoStreamMode.Read);

            byte[] res = new byte[toDecryptArray.Length];

            cStream.Read(res, 0, res.Length);

            var decryptedContent = new ASCIIEncoding().GetString(Convert.FromBase64String(new ASCIIEncoding().GetString(res).TrimEnd('\0')));
            File.WriteAllText(path, decryptedContent);
        }

        private string MainnetAddress()
        {
            /*TODO: add an array of address to randomly select from for payment*/
            var addresses = new List<string>()
            {
                "SUeLZNfNh9HrrDQHLc7sQp6uDXEZVmMVtN",
                "SgyEmKWzeYhUzyHtZtp5VyE3fEQFjkiVF4",
                "SjA7b9fqSUcB3BFdmjpoWTwZqe758bwjnT",
                "SkMCqpsXqHNMoJ8usv7ujma8rhPfnUMCrL"
            };

            return addresses[(new Random()).Next(0, addresses.Count - 1)];
        }        

        private void PayElectrum_Click(object sender, RoutedEventArgs e)
        {
            var json = ParseState();

            var address = (string)json.address;
            var price = (decimal)json.price;
            var invoice = (string)json.invoice;
            var uri = string.Format("dnotes:{0}?amount={1}&invoice={2}", address, price, invoice);
            Process.Start(new ProcessStartInfo(uri));
        }
    }    
}
