using System;
using System.Data;
using System.Linq;
using System.Xml;
using System.Windows.Forms.DataVisualization.Charting;

namespace netGrapher
{
    class Program
    {
        public static void Main(string[] args)
        {
        Start:
            // Temp innan settings är implementerat
            string[] counters = { "Cisco SIP", "Cisco MGCP Gateways", "Cisco MGCP PRI Device" };
            string[] servers = { "127.0.0.1", "127.0.0.2" };

            // Skapa delimiters, devicearray, devicestring, devicevalue innan foreach så att ContainsKey funkar
            var delimiters = new char[] { '(', ')' };
            string devicestring = null;
            int devicevalue = 0;
            // En Dictionary<string, int> för att summera trafikinformationen i
            var result = new System.Collections.Generic.Dictionary<string, int>();
            // loopa igenom alla counters och servrar och spara resultatet result<string, int>
            foreach (var counter in counters)
            {
                foreach (var server in servers)
                {
                    try
                    {
                        // Using används för att stänga resursen efter att den har använts eller något skiter sig
                        using (var client = new System.Net.WebClient())
                        {
                            //var url = string.Format(@"http://{0}:443/perfmonservice/services/PerfmonPort",server); // Använd mot CUCM
                            var url = string.Format(@"http://{0}/Temporary_Listen_Addresses/", server); // Använd vid test mot Simulationserver
                            client.Headers.Add("SOAPAction", "perfmonCollectCounterData");
                            var response = client.UploadString(url, "POST", counter);
                            var xmlResponse = new XmlDocument();
                            xmlResponse.LoadXml(response.ToString());

                            // Letar upp alla "item" taggar i xml-svaret och lopar igenom så att vi kan hantera värdet.
                            XmlNodeList nodelist = xmlResponse.GetElementsByTagName("item");

                            foreach (XmlNode node in nodelist)
                            {
                                switch (counter)
                                {
                                    case "Cisco SIP":
                                        devicestring = System.Text.RegularExpressions.Regex.Match(node["Name"].InnerXml, @"^.*Cisco SIP\((.*)\)\\CallsInProgress$").Groups[1].Value;
                                        break;
                                    case "Cisco MGCP Gateways":
                                        devicestring = System.Text.RegularExpressions.Regex.Match(node["Name"].InnerXml, @"^.*Cisco MGCP Gateways\((.*)\)\\PRIChannelsActive$").Groups[1].Value;
                                        break;
                                    case "Cisco MGCP PRI Device":
                                        devicestring = System.Text.RegularExpressions.Regex.Match(node["Name"].InnerXml, @"^.*Cisco MGCP PRI Device\((.*)\)\\CallsActive$").Groups[1].Value;
                                        break;
                                    default:
                                        throw new Exception("Invalid counter: " + counter);
                                }

                                devicevalue = Convert.ToInt32(node["Value"].InnerXml);

                                // Om regex matchningen inte får träff så är devicestring tom. Vi vill inte lägga till en tom devicestring i resultDict
                                if (devicestring != "")
                                {
                                    if (result.ContainsKey(devicestring))
                                    {
                                        Console.WriteLine("Uppdating: " + devicestring + " " + devicevalue);
                                        result[devicestring] += devicevalue;
                                    }
                                    else
                                    {
                                        Console.WriteLine("Adding: " + devicestring + " " + devicevalue);
                                        result.Add(devicestring, devicevalue);
                                    }
                                }
                            }
                        }
                    }
                    catch (System.Net.WebException e)
                    {

                        Console.WriteLine(e.GetType() + " " + e.Message + " " + server);
                    }
                }
            }

            // Gör något vettigt med resultatet
            // 1. Skapa en teafile för varje device och lägg in aktuell data i den
            foreach (var item in result)
            {

            }

            // 2. Skapa en DS för att spara datat
            var ds = new System.Data.DataSet();
            // 2.5 Försök läs in tidigare sparad databas.xml
            try
            {
                Console.WriteLine("Loading database.xml");
                ds.ReadXml("database.xml");
            }
            catch (System.IO.FileNotFoundException e)
            {
                Console.WriteLine(e.GetType() + " " + e.Message);
                Console.WriteLine("Creating a new database.xml");
            }
            catch (System.Xml.XmlException e)
            {
                Console.WriteLine(e.GetType() + " " + e.Message);
                Console.WriteLine("database.xml is broken so I am saving a backup and creating a new one");
                System.IO.File.Move("database.xml", "database.xml.broken");

            }

            // 3. Loopa igenom result för att mata in datat i ds
            foreach (var item in result)
            {
                // Kolla om det finns en tabell i DS och skapa om inte
                if (ds.Tables.Contains(item.Key))
                {
                    // Tabellen finns så lägg bara till en ny rad.
                    var dr = ds.Tables[item.Key].NewRow();
                    dr[0] = DateTime.Now;
                    dr[1] = (int)item.Value;
                    //dr[2] = ds.Tables[item.Key].Compute("Max(Value)", String.Empty); <--- Gammal sätt
                    dr[2] = ds.Tables[item.Key].AsEnumerable().Max(r => Convert.ToInt32(r.Field<string>("Value"))); // <---- Nytt sätt
                    ds.Tables[item.Key].Rows.Add(dr);
                }
                else
                {
                    // Tabellen finns inte så skapa en ny tabell och lägg till värdet i den.
                    var dt = new System.Data.DataTable(item.Key.ToString());
                    dt.Columns.Add("Timestamp", typeof(DateTime));
                    dt.Columns.Add("Value", typeof(int));
                    dt.Columns.Add("Peak", typeof(int));
                    var dr = dt.NewRow();
                    dr[0] = DateTime.Now;
                    dr[1] = (int)item.Value;
                    dr[2] = dr[1]; // Eftersom att det är en ny tabell så blir ju peak samma som nuvarande värde.
                    dt.Rows.Add(dr);
                    ds.Tables.Add(dt);
                }
            }
            // 4. Spara databasen för framtida bruk
            ds.WriteXml("database.xml");

            // 5. Skapa en graf per tabell i datasetet
            foreach (System.Data.DataTable table in ds.Tables)
            {
                var chart = new Chart();
                chart.DataSource = table;
                chart.Width = 1200;
                chart.Height = 400;
                chart.Titles.Add(table.TableName.ToString());

                // 5.1 Skapa en serie till grafen
                var serie = new Series();
                serie.Name = "Calls";
                serie.ChartType = SeriesChartType.SplineArea;
                serie.Color = System.Drawing.Color.Green;
                serie.BorderColor = System.Drawing.Color.DarkGreen;
                serie.BorderWidth = 4;
                serie.IsValueShownAsLabel = false;
                serie.XValueMember = "Timestamp";
                serie.YValueMembers = "Value";
                // 5.1.1 Skapa en maxvärde serie
                var serieMax = new Series();
                serieMax.Name = "Peak";
                serieMax.ChartType = SeriesChartType.Line;
                serieMax.Color = System.Drawing.Color.Red;
                serieMax.BorderColor = System.Drawing.Color.DarkRed;
                serieMax.BorderWidth = 4;
                serieMax.IsValueShownAsLabel = false;
                serieMax.XValueMember = "Timestamp";
                serieMax.YValueMembers = "Peak";
                // 5.1.2 Lägg till serierna i charten
                chart.Series.Add(serieMax);
                chart.Series.Add(serie);
                // 5.2 Skapa ett grafområde
                var ca = new ChartArea();
                ca.Name = table.ToString();
                //ca.BackColor = Color.White;
                //ca.BorderColor = System.Drawing.Color.Black;
                //ca.BorderWidth = 0;
                //ca.BorderDashStyle = ChartDashStyle.Solid;
                ca.AxisX = new Axis();
                ca.AxisY = new Axis();
                ca.AxisX.IsMarginVisible = false;
                //ca.AxisX.ScaleView.Size = 10;
                ca.AxisY.Interval = 10;
                //ca.AxisX.IsReversed = true;
                //ca.AxisX.Maximum = 288;
                chart.ChartAreas.Add(ca);

                // 5.3 Skapa en legend
                var legend = new Legend();
                legend.IsTextAutoFit = true;
                chart.Legends.Add(legend);

                // 5.4 databind ??
                chart.DataBind();

                // 5.5 Spara som en png.
                chart.SaveImage(table.ToString() + ".png", ChartImageFormat.Png);
            }


            // Det absolut sista som händer. Bör tas bort innan release
            Console.WriteLine("Done. Sover i 10 och kör igen");
            System.Threading.Thread.Sleep(10000);
            goto Start;
            //Console.ReadKey(true);

        }
    }
}