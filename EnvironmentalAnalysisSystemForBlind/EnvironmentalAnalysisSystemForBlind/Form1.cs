﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
//EmguCV
using Emgu.CV;
using Emgu.Util;
using Emgu.CV.UI;
using Emgu.CV.Structure;
using Emgu.CV.Features2D;

using SURFMethond;
namespace EnvironmentalAnalysisSystemForBlind
{
    public partial class Form1 : Form
    {
        //取得專案執行黨所在的目錄=>System.Windows.Forms.Application.StartupPath
        //使用DirectoryInfo移動至上層
        DirectoryInfo dir;
        Capture videoCapture;
        Timer trainingVideoTimer;
        Timer matchingVideoTimer;
        int FPS = 30;
        int trainingVideoTotalFrame,matchingVideoTotalFrame;
        bool isPlay, isSuspend, isStop,isScroll;
        Image<Bgr, byte> queryFrame;
        
        int trainingScrollValue,matchSrollValue;
        Graphics g; //draw rectangle
        Point pressedToDrawPoint;
        bool isPressed;
        Rectangle extractFeatureMaskROI;
        Image<Bgr, byte> extractFeatureImage;
        Image<Bgr, byte> senceImage;
        SURFFeatureData trainingExtractSurfData;
        SURFFeatureData matchingModelSurfData;
        ImageViewer matchViewer;
        public Form1()
        {
            InitializeComponent();
            
            trainingVideoTimer = new Timer();
            matchingVideoTimer = new Timer();
            dir = new DirectoryInfo(System.Windows.Forms.Application.StartupPath);
            trainingVideoTotalFrame = 0;
            isScroll = isPlay = isSuspend = isStop = false;
            isPressed = false;
            matchViewer = new ImageViewer();
            matchViewer.FormClosing += matchViewer_FormClosing;
        }

        void matchViewer_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true; //關閉視窗時取消
            matchViewer.Hide(); //隱藏式窗,下次再show出
        }

        void trainingVideoTimer_Tick(object sender, EventArgs e)
        {
            //如果有影片
            if (videoCapture != null)
            {
                if (isPlay)
                {
                    lock (this)
                    {
                        if (isScroll)
                        {
                            //設定要移動到的frame
                            //http://stackoverflow.com/questions/20902323/get-specific-frames-using-emgucv
                            videoCapture.SetCaptureProperty(Emgu.CV.CvEnum.CAP_PROP.CV_CAP_PROP_POS_FRAMES, trainingScrollValue);
                            isScroll = false;
                        }
                        //如果Frame的index沒有差過影片的最大index
                        if (videoCapture.GetCaptureProperty(Emgu.CV.CvEnum.CAP_PROP.CV_CAP_PROP_POS_FRAMES) < trainingVideoTotalFrame)
                        {
                            //顯示
                            queryFrame = videoCapture.QueryFrame();
                            trainingVideoPictureBox.Image = queryFrame.ToBitmap();
                        }
                    }
                   
                }
                else if (isSuspend) {
                    //擷取想要的區塊 做SURF
                    g = trainingVideoPictureBox.CreateGraphics();
                    

                }
                else if(isStop){
                    //關閉，回到一開始畫面
                    videoCapture.SetCaptureProperty(Emgu.CV.CvEnum.CAP_PROP.CV_CAP_PROP_POS_AVI_RATIO, 0);
                }
            }
        }

        private void trainingLoadVideoButton_Click(object sender, EventArgs e)
        {
            string videoFilename = OpenVideo();
            if (videoFilename != string.Empty) {
                videoCapture = new Capture(videoFilename);
                trainingVideoTotalFrame = (int)videoCapture.GetCaptureProperty(Emgu.CV.CvEnum.CAP_PROP.CV_CAP_PROP_FRAME_COUNT); //Get total frame number
                
                //第一張做影片的封面
                queryFrame = videoCapture.QueryFrame();
                trainingVideoPictureBox.Image = queryFrame.Copy().ToBitmap();
                //設定刻度
                trainingVideoTrackBar.TickStyle = TickStyle.Both;
                trainingVideoTrackBar.Minimum = 0;
                trainingVideoTrackBar.TickFrequency = 1;
                trainingVideoTrackBar.Maximum = trainingVideoTotalFrame;
                
                //設定播放用的Timer
                trainingVideoTimer.Tick += trainingVideoTimer_Tick;
                trainingVideoTimer.Interval = 1000 / FPS;
                trainingVideoTimer.Start();
            }
        }

        private string OpenVideo() {
            OpenFileDialog dlg = new OpenFileDialog();
            //移動上層在指定下層路徑
            dlg.RestoreDirectory = true;
            dlg.InitialDirectory = dir.Parent.Parent.FullName + @"\TrainingVideo";
            dlg.Title = "Open Video File";

            // Set filter for file extension and default file extension
            dlg.Filter = "AVI Video|*.avi";

            // Display OpenFileDialog by calling ShowDialog method ->ShowDialog()
            // Get the selected file name and display in a TextBox
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && dlg.FileName != null)
            {
                // Open document
                string filename = dlg.FileName;
                return filename;
            }
            else
            {
                return null;
            }
        }

        private void playVideoButton_Click(object sender, EventArgs e)
        {
            isPlay = true;
            isSuspend = isStop = false;
        }

        private void suspendButton_Click(object sender, EventArgs e)
        {
            isPlay = isStop = false;
            isSuspend = true;
        }

        private void stopButton_Click(object sender, EventArgs e)
        {
            isStop = true;
            isPlay = isSuspend = false;
        }

        private void trainingVideoTrackBar_ValueChanged(object sender, EventArgs e)
        {
            trainingScrollValue = trainingVideoTrackBar.Value;
            isScroll = true;
        }

        private void trainingVideoPictureBox_MouseUp(object sender, MouseEventArgs e)
        {
            isPressed = false;
        }

        private void trainingVideoPictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            //繪製要擷取的ROI
            if (videoCapture != null && isSuspend)
            {
                if (pressedToDrawPoint != null && isPressed)
                {
                    trainingVideoPictureBox.Image = queryFrame.Copy().ToBitmap();
                    using (Graphics g = Graphics.FromImage(trainingVideoPictureBox.Image))
                    {
                        //取得ROI座標
                        extractFeatureMaskROI = new Rectangle(pressedToDrawPoint.X, pressedToDrawPoint.Y, Math.Abs(e.X - pressedToDrawPoint.X), Math.Abs(e.Y - pressedToDrawPoint.Y));
                        g.DrawRectangle(new Pen(Brushes.Red, 5), extractFeatureMaskROI);
                    }
                    try
                    {
                        //指定要在畫面上顯示的ROI大小
                        wantExtractFeatureImageBox.Width = extractFeatureMaskROI.Width;
                        wantExtractFeatureImageBox.Height = extractFeatureMaskROI.Height;
                        //取得影像中的ROI影像
                        Image<Bgr, byte> roi = queryFrame.Copy();
                        roi.ROI = extractFeatureMaskROI; //如此指定影像變繪製取得ROI
                        //取得要處理的影像
                        extractFeatureImage = roi.Copy();
                        //顯示出ROI
                        wantExtractFeatureImageBox.Image = roi;
                        //重繪
                        trainingVideoPictureBox.Invalidate();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error:" + ex.Message);
                    }
                    
                }
            }
        }

        private void trainingVideoPictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (queryFrame != null)
            {
                pressedToDrawPoint = e.Location;
                isPressed = true;
                //如果畫完又再次點及其他位置，會再重新拿取乾淨畫面，重新繪製別的ROI
                trainingVideoPictureBox.Image = queryFrame.Copy().ToBitmap();
            }
        }

        private void extractFeatureButton_Click(object sender, EventArgs e)
        {
            trainingExtractSurfData = SURFMatch.CalSURFFeature(extractFeatureImage, new MCvSURFParams(500, false));
            //繪製特徵
            Image<Bgr, byte> result = Features2DToolbox.DrawKeypoints(trainingExtractSurfData.GetImg(), trainingExtractSurfData.GetKeyPoints(), new Bgr(255, 255, 255), Features2DToolbox.KeypointDrawType.DEFAULT);
            //顯示
            ImageViewer viewer = new ImageViewer(result);
            viewer.Show();
        }

        private void saveFeatureDataButton_Click(object sender, EventArgs e)
        {
            SaveDescriptorFile();
            SaveImgFile(extractFeatureImage.ToBitmap());
            MessageBox.Show("Save feature and extract image successed !");
        }
        //存描述子
        private bool SaveDescriptor(string fileName)
        {
            if (trainingExtractSurfData.GetDescriptors() != null)
            {
                string format = Path.GetExtension(fileName);
                if (format == ".xml") WriteSURFFeatureDataToBinaryXml(trainingExtractSurfData, fileName);
                //Console Output觀看數值
                Console.WriteLine("Save Descriptor Data........\n");
                Console.WriteLine("\n");
                return true;
            }
            return false;
        }

        //Write Xml
        public static void WriteSURFFeatureDataToBinaryXml(SURFFeatureData surf, string TextFileName)
        {
            Stream stream;
            System.Runtime.Serialization.Formatters.Binary.BinaryFormatter bformatter;
            //寫檔
            try
            {
                // serialize histogram
                stream = File.Open(TextFileName, FileMode.Create);
                bformatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                bformatter.Serialize(stream, surf);
                stream.Close();
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException(ex.Message);
            }
        }

        private void SaveDescriptorFile()
        {
            // Displays a SaveFileDialog so the user can save the Image
            // assigned to Button2.
            //Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog(); //WPF
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "XML Files (*.xml)|*.xml";
            dlg.Title = "Save SURF Descriptor to File";
            dlg.RestoreDirectory = true;
            dlg.InitialDirectory = dir.Parent.Parent.FullName + "\\FeartureDataFile\\SURFDescriptorDataFile\\";

            // If the file name is not an empty string open it for saving.
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && dlg.FileName != "")
            {
                bool isOk = SaveDescriptor(dlg.FileName);
                if (isOk) MessageBox.Show("SaveDescriptorFile Ok");
                else MessageBox.Show("SaveDescriptorFile Faild");
            }
        }

        private bool SaveImgFile(Bitmap bitmap)
        {
            // Displays a SaveFileDialog so the user can save the Image
            // assigned to Button2.
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "JPeg Image|*.jpg|Bitmap Image|*.bmp|Gif Image|*.gif|Png Image|*.png";
            dlg.Title = "Save SURF Image File";
            // If the file name is not an empty string open it for saving.
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && dlg.FileName != "")
            {
                switch (dlg.FilterIndex)
                {
                    case 1:
                        bitmap.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Jpeg);
                        break;

                    case 2:
                        bitmap.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Bmp);
                        break;

                    case 3:
                        bitmap.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Gif);
                        break;
                    case 4:
                        bitmap.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png);
                        break;
                }
                return true;
            }
            return false;
        }

        private void stopMatchingVideoButton_Click(object sender, EventArgs e)
        {
            isPlay = false;
            isStop = true;
        }

        private void playMatchingVideoButton_Click(object sender, EventArgs e)
        {
            isPlay = true;
            isStop = false;
        }

        private void loadMatchingVideoButton_Click(object sender, EventArgs e)
        {
            string videoFilename = OpenVideo();
            if (videoFilename != string.Empty)
            {
                videoCapture = new Capture(videoFilename);
                matchingVideoTotalFrame = (int)videoCapture.GetCaptureProperty(Emgu.CV.CvEnum.CAP_PROP.CV_CAP_PROP_FRAME_COUNT); //Get total frame number

                //第一張做影片的封面
                senceImage = videoCapture.QueryFrame();
                mathcingVideoPictureBox.Image = senceImage.Copy().ToBitmap();

                //設定刻度
                matchingVideoTrackBar.TickStyle = TickStyle.Both;
                matchingVideoTrackBar.Minimum = 0;
                matchingVideoTrackBar.TickFrequency = 1;
                matchingVideoTrackBar.Maximum = trainingVideoTotalFrame;

                matchingVideoTimer.Tick += matchingVideoTimer_Tick;
                matchingVideoTimer.Interval = 1000 / FPS;
                matchingVideoTimer.Start();
            }
        }

        void matchingVideoTimer_Tick(object sender, EventArgs e)
        {
            //如果有影片
            if (videoCapture != null)
            {
                if (isPlay)
                {
                    lock (this)
                    {
                        if (isScroll)
                        {
                            //設定要移動到的frame
                            //http://stackoverflow.com/questions/20902323/get-specific-frames-using-emgucv
                            videoCapture.SetCaptureProperty(Emgu.CV.CvEnum.CAP_PROP.CV_CAP_PROP_POS_FRAMES, matchSrollValue);
                            isScroll = false;
                        }
                        //如果Frame的index沒有差過影片的最大index
                        if (videoCapture.GetCaptureProperty(Emgu.CV.CvEnum.CAP_PROP.CV_CAP_PROP_POS_FRAMES) < matchingVideoTotalFrame)
                        {
                            //顯示
                            senceImage = videoCapture.QueryFrame();
                            //Match                        
                            MatchImage(senceImage, ReadSURFFeature(dir.Parent.Parent.FullName + "\\SurfFeatureData\\000-0.xml"));

                            mathcingVideoPictureBox.Image = senceImage.ToBitmap();
                        }
                    }
                   

                   
                }
                else if (isStop)
                {
                    //關閉，回到一開始畫面
                    videoCapture.SetCaptureProperty(Emgu.CV.CvEnum.CAP_PROP.CV_CAP_PROP_POS_AVI_RATIO, 0);
                }
            }
        }

        private void MatchImage(Image<Bgr, byte> senceImg, SURFFeatureData modelSurfData) {
            if (modelSurfData != null)
            {
                SURFFeatureData senceSurfData =  SURFMatch.CalSURFFeature(senceImage);
                Image<Bgr,byte> result = SURFMatch.MatchSURFFeatureByBF(modelSurfData, senceSurfData);
                if (result != null)
                {
                    matchViewer.Image = result;
                    matchViewer.Show();
                }
            }
        
        }

        public SURFFeatureData ReadSURFFeature(string fileName)
        {
            matchingModelSurfData = ReadSURFFeatureDataFromBinaryXml(fileName);
            Console.WriteLine("Read Descriptor Data........\n");
            // ConsoleOutputMethod.ShowDescriptorDataOnConsole(descriptor);
            Console.WriteLine("\n");
            return matchingModelSurfData;
        }

        /// <summary>
        /// 讀取SURFFeature類別,寫入的資料是序列Byte
        /// </summary>
        /// <param name="TextFileName"></param>
        /// <returns></returns>
        public static SURFFeatureData ReadSURFFeatureDataFromBinaryXml(string TextFileName)
        {
            Stream stream;
            System.Runtime.Serialization.Formatters.Binary.BinaryFormatter bformatter;
            SURFFeatureData templateSurf;
            try
            {
                if (File.Exists(TextFileName))
                {
                    stream = File.Open(TextFileName, FileMode.Open);
                    bformatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                    templateSurf = (SURFFeatureData)bformatter.Deserialize(stream);
                    stream.Close();
                    return templateSurf;
                }
                return null;

            }
            catch (IOException ex)
            {
                throw new InvalidOperationException(ex.Message);
            }
        }

        private void matchingVideoTrackBar_ValueChanged(object sender, EventArgs e)
        {
            matchSrollValue = matchingVideoTrackBar.Value;
            isScroll = true;
        }
    }
}
