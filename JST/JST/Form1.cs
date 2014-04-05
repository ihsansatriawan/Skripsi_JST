using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using System.IO;
using System.ComponentModel;
using System.Text;

using AForge;
using AForge.Neuro;
using AForge.Neuro.Learning;
using AForge.Controls;

namespace JST
{
    public partial class Form1 : Form
    {
        private int samples = 0;
        private int hneuron = 2;
        private int variables = 0;
        private double[,] data = null;
        private int[] classes = null;
        private int classesCount = 0;
        private double learningRate = 0.1;
        private double maxLearningError = 0.1;
        private double maxepoch = 1000;
        private bool saveStatisticsToFiles = false;
        private Thread workerThread = null;
        private volatile bool needToStop = false;

        public Form1()
        {
            InitializeComponent();
            UpdateSettings();
        }

        // Delegates to enable async calls for setting controls properties
        private delegate void SetTextCallback(System.Windows.Forms.Control control, string text);
        private delegate void ClearListCallback(System.Windows.Forms.ListView control);
        private delegate ListViewItem AddListItemCallback(System.Windows.Forms.ListView control, string itemText);
        private delegate void AddListSubitemCallback(ListViewItem item, string subItemText);

        // Thread safe updating of control's text property
        private void SetText(System.Windows.Forms.Control control, string text)
        {
            if (control.InvokeRequired)
            {
                SetTextCallback d = new SetTextCallback(SetText);
                Invoke(d, new object[] { control, text });
            }
            else
            {
                control.Text = text;
            }
        }

        // Thread safe clearing of list view
        private void ClearList(System.Windows.Forms.ListView control)
        {
            if (control.InvokeRequired)
            {
                ClearListCallback d = new ClearListCallback(ClearList);
                Invoke(d, new object[] { control });
            }
            else
            {
                control.Items.Clear();
            }
        }

        // Thread safe adding of item to list control
        private ListViewItem AddListItem(System.Windows.Forms.ListView control, string itemText)
        {
            ListViewItem item = null;

            if (control.InvokeRequired)
            {
                AddListItemCallback d = new AddListItemCallback(AddListItem);
                item = (ListViewItem)Invoke(d, new object[] { control, itemText });
            }
            else
            {
                item = control.Items.Add(itemText);
            }

            return item;
        }

        // Thread safe adding of subitem to list control
        private void AddListSubitem(ListViewItem item, string subItemText)
        {
            if (this.InvokeRequired)
            {
                AddListSubitemCallback d = new AddListSubitemCallback(AddListSubitem);
                Invoke(d, new object[] { item, subItemText });
            }
            else
            {
                item.SubItems.Add(subItemText);
            }
        }

        private void MainForm_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // check if worker thread is running
            if ((workerThread != null) && (workerThread.IsAlive))
            {
                needToStop = true;
                while (!workerThread.Join(100))
                    Application.DoEvents();
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            OpenFileDialog FD = new OpenFileDialog();
            FD.Title = "File Data Bayi Berat Lahir Rendah";
            FD.Filter = "CSV (Comma Delimited)(*.csv)|*.csv";

            if (FD.ShowDialog() == DialogResult.OK)
            {
                StreamReader reader = null;

                // temp buffers (for 200 samples only)
                double[,] tempData = null;
                int[] tempClasses = new int[200];

                // min and max X values
                double minX = double.MaxValue;
                double maxX = double.MinValue;

                // samples and classes count
                samples = 0;
                classesCount = 0;

                try
                {
                    string str = null;

                    // open selected file
                    reader = File.OpenText(FD.FileName);

                    // read the data
                    while ((samples < 200) && ((str = reader.ReadLine()) != null))
                    {
                        // split the string
                        string[] strs = str.Split(';');
                        if (strs.Length == 1)
                            strs = str.Split(',');

                        // allocate data array
                        if (samples == 0)
                        {
                            variables = strs.Length - 1;
                            tempData = new double[200, variables];
                        }

                        // parse data
                        for (int j = 0; j < variables; j++)
                        {
                            tempData[samples, j] = double.Parse(strs[j]);
                        }
                        tempClasses[samples] = int.Parse(strs[variables]);

                        if (tempClasses[samples] >= classesCount)
                            classesCount = tempClasses[samples] + 1;

                        // search for min value
                        if (tempData[samples, 0] < minX)
                            minX = tempData[samples, 0];
                        // search for max value
                        if (tempData[samples, 0] > maxX)
                            maxX = tempData[samples, 0];

                        samples++;
                    }

                    // allocate and set data
                    data = new double[samples, variables];
                    Array.Copy(tempData, 0, data, 0, samples * variables);
                    classes = new int[samples];
                    Array.Copy(tempClasses, 0, classes, 0, samples);
                }

                catch (Exception)
                {
                    MessageBox.Show("Failed reading the file", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                finally
                {
                    // close file
                    if (reader != null)
                        reader.Close();
                }

                // update list and chart
                UpdateDataListView();
                string Filename = FD.FileName;
                if (Path.GetExtension(Filename) == ".csv")
                {
                    textBox1.Text = Filename;
                    textBox2.Text = string.Format("{0}", samples);
                }
                textBox9.Text = classesCount.ToString();
                button2.Enabled = true;
            }
        }

        private void UpdateSettings()
        {
            textBox4.Text = learningRate.ToString();
            textBox5.Text = maxepoch.ToString();
            textBox10.Text = maxLearningError.ToString();
            textBox12.Text = hneuron.ToString();
            saveFilesCheck.Checked = saveStatisticsToFiles;
        }

        private void UpdateDataListView()
        {
            // remove all curent data and columns
            dataList.Items.Clear();
            dataList.Columns.Clear();

            // add columns
            for (int i = 0, n = variables; i < n; i++)
            {
                dataList.Columns.Add(string.Format("X{0}", i + 1),
                   40, HorizontalAlignment.Left);
            }
            dataList.Columns.Add("Kelas", 40, HorizontalAlignment.Left);

            // add items
            for (int i = 0; i < samples; i++)
            {
                dataList.Items.Add(data[i, 0].ToString());

                for (int j = 1; j < variables; j++)
                {
                    dataList.Items[i].SubItems.Add(data[i, j].ToString());
                }
                dataList.Items[i].SubItems.Add(classes[i].ToString());
            }
        }

        // Delegates to enable async calls for setting controls properties
        private delegate void EnableCallback(bool enable);

        // Enable/disale controls (safe for threading)
        private void EnableControls(bool enable)
        {
            if (InvokeRequired)
            {
                EnableCallback d = new EnableCallback(EnableControls);
                Invoke(d, new object[] { enable });
            }
            else
            {
                textBox3.Enabled = !enable;
                textBox11.Enabled = !enable;
                textBox4.Enabled = enable;
                textBox5.Enabled = enable;
                textBox10.Enabled = enable;
                textBox12.Enabled = enable;
                button1.Enabled = enable;
                button2.Enabled = enable;
                saveFilesCheck.Enabled = enable;
                button3.Enabled = !enable;
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            // get learning rate
            try
            {
                learningRate = Math.Max(0.00001, Math.Min(1, double.Parse(textBox4.Text)));
            }
            catch
            {
                learningRate = 0.1;
            }

            try
            {
                maxepoch = Math.Max(0, int.Parse(textBox5.Text));
            }
            catch
            {
                maxepoch = 100000;
            }

            try
            {
                maxLearningError = Math.Max(0, double.Parse(textBox10.Text));
            }
            catch
            {
                maxLearningError = 0.1;
            }

            try
            {
                hneuron = Math.Max(0, int.Parse(textBox12.Text));
            }
            catch
            {
                hneuron = 2;
            }

            saveStatisticsToFiles = saveFilesCheck.Checked;

            // update settings controls
            UpdateSettings();

            // disable all settings controls
            EnableControls(false);

            // run worker thread
            needToStop = false;
            workerThread = new Thread(new ThreadStart(SearchSolution));
            workerThread.Start();

        }
        
        private void button3_Click(object sender, EventArgs e)
        {
            // stop worker thread
            needToStop = true;
            while (!workerThread.Join(100))
                Application.DoEvents();
            workerThread = null;
        }

        // Worker thread
        public void SearchSolution()
        {
            // prepare learning data
            double[][] input = new double[samples][];
            double[][] hidden = new double[samples][];
            double[][] output = new double[samples][];

            for (int i = 0; i < samples; i++)
            {
                input[i] = new double[variables];
                hidden[i] = new double[hneuron];
                output[i] = new double[1];

                // copy input
                for (int j = 0; j < variables; j++)
                    input[i][j] = data[i, j];
                // copy output
                output[i][0] = classes[i];
            }

            // create backpropagation
            ActivationNetwork network = new ActivationNetwork(new SigmoidFunction(2), variables, hneuron, 1);
            ActivationNeuron neuron = network[0][0];
            ActivationLayer layer = network[0];
            // create teacher
            BackPropagationLearning teacher = new BackPropagationLearning(network);
            // set learning rate
            teacher.LearningRate = learningRate;
            // iterations
            int iteration = 1;

            // statistic files
            StreamWriter errorsFile = null;
            StreamWriter weightsFile = null;

            try
            {
                // check if we need to save statistics to files
                if (saveStatisticsToFiles)
                {
                    // open files
                    errorsFile = File.CreateText("C:\\aaaaaa\\program\\s\\error.csv");
                    weightsFile = File.CreateText("C:\\aaaaaa\\program\\s\\weights.csv");
                }

                // erros list
                ArrayList errorsList = new ArrayList();

                // loop
                while (!needToStop)
                {
                    // run epoch of learning procedure
                    double error = teacher.RunEpoch(input, output);
                    errorsList.Add(error);
                    
                    // save current weights
                    if (weightsFile != null)
                    {
                        weightsFile.Write("\n" + "iterasi" + iteration);
                        for (int i = 0; i < 1; i++)
                        {
                            weightsFile.Write("\n" + "nilai bobot-HO = " + layer[i].Threshold);
                            for (int j = 0; j < hneuron; j++)
                            {
                                weightsFile.Write("\n" + "nilai bobot-IH neuron " + j + "=" + layer[j].Threshold);
                                weightsFile.Write("\n" + "IHneuron" + j + "," + "HOneuron" + j);
                                for (int k = 0; k < variables; k++)
                                {
                                    weightsFile.Write("\n" + layer[j][k] + "," + layer[i][j]);
                                }
                            }
                        }
                    }

                    // save current error
                    if (errorsFile != null)
                    {
                        errorsFile.WriteLine(error);
                    }

                    // show current iteration
                    SetText(textBox3, iteration.ToString());
                    SetText(textBox11, error.ToString());


                    // stop if no error
                    if (error < maxLearningError || iteration >= maxepoch)
                        break;
                    iteration++;
                }

                // show Backpropagation's weights
                ClearList(weightsList);
                for (int i = 0; i < 1; i++)
                {
                    string neuro = string.Format("neuron {0}", i + 1);
                    ListViewItem item = null;

                    for (int j = 0; j < hneuron; j++)
                    {
                        item = AddListItem(weightsList, neuro);
                        AddListSubitem(item, string.Format("HOWeight {0}", j + 1));
                        AddListSubitem(item, layer[i][j].ToString("F6"));
                        for (int k = 0; k < variables; k++)
                        {
                            item = AddListItem(weightsList, neuro);
                            AddListSubitem(item, string.Format("IHWeight {0}", k + 1));
                            AddListSubitem(item, layer[j][k].ToString("F6"));
                        }
                        item = AddListItem(weightsList, neuro);
                        AddListSubitem(item, string.Format("IHthreshold{0}", j + 1));
                        AddListSubitem(item, layer[j].Threshold.ToString("F6"));
                    }
                    item = AddListItem(weightsList, neuro);
                    AddListSubitem(item, string.Format("HOthreshold{0}", i + 1));
                    AddListSubitem(item, layer[i].Threshold.ToString("F6"));
                }
                network.Save("D:\\My Documents\\Ihsan's Document\\skripsi\\s\\JST\\net_latih.net");
            }
            catch (IOException)
            {
                MessageBox.Show("Failed writing file", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            finally
            {
                // close files
                if (errorsFile != null)
                    errorsFile.Close();
                if (weightsFile != null)
                    weightsFile.Close();
            }

            // enable settings controls
            EnableControls(true);
        }
        private void button5_Click(object sender, EventArgs e)
        {
            double hasil = 0;
            string hsl = "";
            //Network network = Network.Load("net_latih.bin");
            try
            {
                double x1, x2, x3, x4, x5, x6, x7, x8, x9, x10, x11, x12, x13, x14 = 0;
                x1 = Convert.ToDouble(textBox20.Text);
                x2 = Convert.ToDouble(textBox21.Text);
                x3 = Convert.ToDouble(textBox22.Text);
                x4 = Convert.ToDouble(textBox23.Text);
                x5 = Convert.ToDouble(textBox24.Text);
                x6 = Convert.ToDouble(textBox25.Text);
                x7 = Convert.ToDouble(textBox26.Text);
                x8 = Convert.ToDouble(textBox13.Text);
                x9 = Convert.ToDouble(textBox14.Text);
                x10 = Convert.ToDouble(textBox15.Text);
                x11 = Convert.ToDouble(textBox16.Text);
                x12 = Convert.ToDouble(textBox17.Text);
                x13 = Convert.ToDouble(textBox18.Text);
                x14 = Convert.ToDouble(textBox19.Text);
                
                double[] datauji = new double[] {x1,x2, x3, x4, x5, x6, x7, x8, x9, x10, x11, x12, x13, x14 };
                Network network = (ActivationNetwork)Network.Load("C:\\aaaaaa\\program\\s\\net_latih.net");         
                double []output= network.Compute(datauji);

                for (int x=0;x<output.Length;x++)
                {
                    hasil = Math.Pow(output[x],2)/output[x];
                    hasil = Math.Round(hasil);
                }

                hsl = hsl + String.Format("{0}", hasil);
                textBox27.Text = textBox6.Text;
                textBox28.Text = textBox7.Text;
                textBox29.Text = textBox8.Text;
                switch (hsl)
                {
                    case"0":
                        textBox30.Text = "kemampuan bertahan bayi : Tinggi";
                        break;
                    case "1":
                        textBox30.Text = "kemampuan bertahan bayi : rendah";
                        break;
                    default:
                        textBox30.Text = "tidak terdefinisi";
                        break;
                }
            }
            catch (FormatException)
            {
                MessageBox.Show("Silakan Masukkan Data Dengan Benar", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #region
        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox1.SelectedIndex == 0)
                textBox20.Text = "0";
            else
                textBox20.Text = "1";
        }

        private void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox2.SelectedIndex == 0)
                textBox21.Text = "0";
            else if (comboBox2.SelectedIndex == 1)
                textBox21.Text = "0.25";
            else if (comboBox2.SelectedIndex == 2)
                textBox21.Text = "0.5";
            else if (comboBox2.SelectedIndex == 3)
                textBox21.Text = "0.75";
            else
                textBox21.Text = "1";
        }

        private void comboBox3_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox3.SelectedIndex == 0)
                textBox22.Text = "0";
            else if (comboBox3.SelectedIndex == 1)
                textBox22.Text = "0.25";
            else if (comboBox3.SelectedIndex == 2)
                textBox22.Text = "0.5";
            else if (comboBox3.SelectedIndex == 3)
                textBox22.Text = "0.75";
            else
                textBox22.Text = "1";
        }

        private void comboBox4_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox4.SelectedIndex == 0)
                textBox23.Text = "0";
            else if (comboBox4.SelectedIndex == 1)
                textBox23.Text = "0.25";
            else if (comboBox4.SelectedIndex == 2)
                textBox23.Text = "0.5";
            else if (comboBox4.SelectedIndex == 3)
                textBox23.Text = "0.75";
            else
                textBox23.Text = "1";
        }

        private void comboBox5_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox5.SelectedIndex == 0)
                textBox24.Text = "0";
            else
                textBox24.Text = "1";
        }

        private void comboBox6_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox6.SelectedIndex == 0)
                textBox25.Text = "0";
            else
                textBox25.Text = "1";
        }

        private void comboBox7_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox7.SelectedIndex == 0)
                textBox26.Text = "0";
            else
                textBox26.Text = "1";
        }

        private void comboBox8_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox8.SelectedIndex == 0)
                textBox13.Text = "0";
            else
                textBox13.Text = "1";
        }

        private void comboBox9_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox9.SelectedIndex == 0)
                textBox14.Text = "0";
            else
                textBox14.Text = "1";
        }

        private void comboBox10_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox10.SelectedIndex == 0)
                textBox15.Text = "0";
            else
                textBox15.Text = "1";
        }

        private void comboBox11_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox11.SelectedIndex == 0)
                textBox16.Text = "0";
            else
                textBox16.Text = "1";
        }

        private void comboBox12_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox12.SelectedIndex == 0)
                textBox17.Text = "0";
            else
                textBox17.Text = "1";
        }

        private void comboBox13_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox13.SelectedIndex == 0)
                textBox18.Text = "0";
            else
                textBox18.Text = "1";
        }

        private void comboBox14_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox14.SelectedIndex == 0)
                textBox19.Text = "0";
            else
                textBox19.Text = "1";
        }
        #endregion

    }
}