///				Apache License
///                         Version 2.0, January 2004
///			http://www.apache.org/licenses/
///				By: Mehdi Amrollahi
using System;
using System.Collections.Generic;
using System.Text;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;
using GsmComm.PduConverter;
using GsmComm.PduConverter.SmartMessaging;
using System.Data;

namespace MSmsLib
{
    private class cls_base
    {
        public const string NEW_LINE = "\r\n";
        public const string OK_FEEDBACK = "\r\nOK\r\n";
        public const string ERROR_FEEDBACK = "\r\nERROR\r\n";

        public const string PATTERN_ERROR = @"^\r\n\+CMS\sERROR:\s[0-9]+\r\n.*";
        public const string PATTERN_SEND_SMS_OK = @"^(\r\n){2}[+]CMGS:\s\d+.*";
        public const string PATTERN_WRITE_SMS = @"^\r\n>\s.*";
    }



    private class cls_Modem : cls_base
    {

        private static cls_Modem m_instance;
        public static cls_Modem p_getInstance
        {
            get
            {
                if (m_instance == null)
                {
                    m_instance = new cls_Modem();
                }
                return m_instance;
            }
        }

        private SerialPort m_modemSerialPort;

        private cls_Modem()
        {
            f_defaultSetting("COM99");
        }

        public bool f_openPort()
        {
            try
            {
                if (!m_modemSerialPort.IsOpen)
                    m_modemSerialPort.Open();

                return true;
            }
            catch
            {

                return false;
            }

        }

		private void f_defaultSetting(string str_portName)
        {
            m_modemSerialPort = new SerialPort(str_portName);
            m_modemSerialPort.BaudRate = 9600;
            m_modemSerialPort.DataBits = 8;
            m_modemSerialPort.StopBits = StopBits.One;
            m_modemSerialPort.ReadTimeout = 3000;
            m_modemSerialPort.WriteTimeout = 2000;
        }


        public bool f_write(string str_cmd)
        {
            try
            {
                m_modemSerialPort.Write(str_cmd);
                return true;
            }
            catch
            {
                return false;
            }
        }


        public bool f_testConnection(string str_portName)
        {
            if (str_portName != m_modemSerialPort.PortName)
            {
                SerialPort obj_tempModem = new SerialPort(str_portName);
                try
                {
                    obj_tempModem.Open();
                    obj_tempModem.Write("AT\r");
                    string str_rep = "";
                    for (int i = 0; i < 15; i++)
                    {
                        Thread.Sleep(50);
                        str_rep += obj_tempModem.ReadExisting();
                    }
                    if (str_rep == OK_FEEDBACK)
                    {
                        m_modemSerialPort.Close();
                        obj_tempModem.Close();

                        this.f_defaultSetting(str_portName);

                        m_modemSerialPort.Open();
                        return m_modemSerialPort.IsOpen;
                    }
                    else
                    {
                        return false;
                    }
                }
                catch (Exception exp)
                {
                    return false;
                }


            }

            return m_modemSerialPort.IsOpen;
        }


        public string f_readFeedback()
        {

            string strFeedback = "";
            List<byte> obj_listRead = new List<byte>();


            DateTime dtNow = DateTime.Now;
            double differ = 0;


            while (!(strFeedback.EndsWith(OK_FEEDBACK)
                || strFeedback.EndsWith(ERROR_FEEDBACK)
                || Regex.IsMatch(strFeedback, cls_base.PATTERN_ERROR)
                || Regex.IsMatch(strFeedback, PATTERN_WRITE_SMS)
                || differ > 25))
            {
                Thread.Sleep(10);
                differ = TimeSpan.FromTicks(DateTime.Now.Ticks).TotalSeconds - TimeSpan.FromTicks(dtNow.Ticks).TotalSeconds;

                try
                {
                    while (m_modemSerialPort.BytesToRead != 0)
                    {
                        int re = m_modemSerialPort.ReadByte();
                        obj_listRead.Add((byte)re);
                    }

                }
                catch (Exception)
                {
                    if (obj_listRead.Count != 0)
                    {
                        strFeedback += Encoding.ASCII.GetString(obj_listRead.ToArray());
                    }

                    break;
                }


                strFeedback += Encoding.ASCII.GetString(obj_listRead.ToArray());
                obj_listRead.Clear();
            }

            return strFeedback;
        }



        


    }
    
    public class cls_smsLib
    {

        enum Enum_logState
        {
            SMS_send_YES,
            SMS_send_NO,
            Command_successfull,
            Command_unSuccessfull,
            Error,
            None

        }

        /// <summary>
        /// semaphore for control the modem ( One instance in a time can catch the modem )
        /// </summary>
        private Semaphore m_semaphoreWriteSMS = new Semaphore(1, 50);

        /// <summary>
        /// class for decode the CMGL responce
        /// </summary>
        class cls_CMGLResponse
        {
            public string p_pdu { get; set; }
            public int p_idx { get; set; }
            public int p_state { get; set; }
            public int p_alpha { get; set; }
            public int p_lenght { get; set; }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>datatable:phone,message,date</returns>
        public DataTable f_getReceivedSMS()
        {            
            if (!cls_Modem.p_getInstance.f_openPort())
            {                
                return null;
            }

            string str_report = "";
            try
            {
                m_semaphoreWriteSMS.WaitOne();

                string strCmd = "at+cmgf=0\r";
                cls_Modem.p_getInstance.f_write(strCmd);
                cls_Modem.p_getInstance.f_readFeedback();

                strCmd = "at+cnmi=1,0,0,1,0\r";
                cls_Modem.p_getInstance.f_write(strCmd);
                cls_Modem.p_getInstance.f_readFeedback();

                strCmd = "at+cpms=\"SM\"\r";
                cls_Modem.p_getInstance.f_write(strCmd);
                cls_Modem.p_getInstance.f_readFeedback();

                strCmd = "at+cmgl=4\r";
                cls_Modem.p_getInstance.f_write(strCmd);
                str_report = cls_Modem.p_getInstance.f_readFeedback();

                m_semaphoreWriteSMS.Release();

            }
            catch (Exception exp)
            {
                f_logReport(Enum_logState.Error, exp.Message + exp.StackTrace);                
                m_semaphoreWriteSMS.Release();
                return null;
            }

            if (Regex.IsMatch(str_report, cls_base.PATTERN_ERROR) || str_report == cls_base.ERROR_FEEDBACK)
            {
                f_logReport(Enum_logState.Error, str_report.Replace(cls_base.NEW_LINE, ""));                
                return null;
            }
            str_report = str_report.Replace(cls_base.OK_FEEDBACK, "");
            if (str_report == "")
            {                
                return null;
            }

            DataTable obj_receivedSMS = new DataTable();
            obj_receivedSMS.Columns.Add("phone");
            obj_receivedSMS.Columns.Add("message");
            obj_receivedSMS.Columns.Add("date");
            
            ///get respnces in parts
            ///
            string[] str_data = str_report.Split(new string[] { "+CMGL: ", cls_base.NEW_LINE }, StringSplitOptions.RemoveEmptyEntries);
            List<cls_CMGLResponse> obj_responses = new List<cls_CMGLResponse>();
            for (int i = 0; i < str_data.Length; i = i + 2)
            {
                string[] obj_strResp = str_data[i].Split(new string[] { "," }, StringSplitOptions.None);
                obj_responses.Add(new cls_CMGLResponse
                {
                    p_pdu = str_data[i + 1]
                    ,
                    p_idx = int.Parse(obj_strResp[0])
                    ,
                    p_state = int.Parse(obj_strResp[1])
                    ,
                    p_alpha = 0
                    ,
                    p_lenght = int.Parse(obj_strResp[3])
                });
            }

            ///this list , contain complete smses to delete from a SIM
            ///
            List<cls_CMGLResponse> obj_deleteResponses = new List<cls_CMGLResponse>();

            ///this list , catch the same smses to find that sms is complete receive or not
            ///
            List<cls_CMGLResponse> obj_tempResponses = new List<cls_CMGLResponse>();

            List<SmsPdu> obj_lstPDUs = new List<SmsPdu>();
            try
            {
                string str_smsText = "";

                for (int i = 0; i < obj_responses.Count; i++)
                {
                    str_smsText = "";
                    IncomingSmsPdu obj_pdu = (IncomingSmsPdu)SmsDeliverPdu.Decode(obj_responses[0].p_pdu, true);
                    bool isComplete = false;

                    obj_tempResponses.Add(obj_responses[i]);
                    obj_lstPDUs.Add(obj_pdu);

                    ///To know sms has other parts or not
                    ///
                    if (!SmartMessageDecoder.AreAllConcatPartsPresent(obj_lstPDUs))
                    {

                        for (int j = i + 1; (j < obj_responses.Count) && !SmartMessageDecoder.AreAllConcatPartsPresent(obj_lstPDUs); j++)
                        {
                            obj_pdu = (SmsDeliverPdu)SmsDeliverPdu.Decode(obj_responses[j].p_pdu, true);
                            if (SmartMessageDecoder.ArePartOfSameMessage(obj_lstPDUs[0], obj_pdu))
                            {
                                obj_tempResponses.Add(obj_responses[j]);
                                obj_lstPDUs.Add(obj_pdu);
                            }

                        }
                        if (SmartMessageDecoder.AreAllConcatPartsPresent(obj_lstPDUs))
                        {
                            str_smsText = SmartMessageDecoder.CombineConcatMessageText(obj_lstPDUs);
                            isComplete = true;
                        }

                    }
                    else
                    {
                        str_smsText = SmartMessageDecoder.CombineConcatMessageText(obj_lstPDUs);
                        isComplete = true;
                    }

                    for (int k = 0; k < obj_tempResponses.Count; k++)
                    {
                        obj_responses.Remove(obj_tempResponses[k]);
                        i--;
                    }

                    ///update DB and datagrid
                    if (isComplete)
                    {
                        obj_deleteResponses.AddRange(obj_tempResponses);

                        DataRow obj_dtRow = obj_receivedSMS.NewRow();
                        obj_dtRow["phone"] = ((SmsDeliverPdu)obj_lstPDUs[0]).OriginatingAddress;
                        obj_dtRow["message"] = str_smsText;
                        obj_dtRow["date"] = ((SmsDeliverPdu)obj_lstPDUs[0]).GetTimestamp().ToDateTime().ToString();

                        obj_receivedSMS.Rows.Add(obj_dtRow);
                        f_logReport(Enum_logState.None, "Message received from \"" + ((SmsDeliverPdu)obj_lstPDUs[0]).OriginatingAddress + "\" " + DateTime.Now.ToLongTimeString());
                    }
                    else
                    {
                        long y = DateTime.Now.Ticks - ((SmsDeliverPdu)obj_lstPDUs[0]).GetTimestamp().ToDateTime().Ticks;
                        if (y > 1800000)
                        {
                            obj_deleteResponses.AddRange(obj_tempResponses);
                        }

                    }

                    obj_tempResponses.Clear();
                    obj_lstPDUs.Clear();
                }

            }
            catch (Exception exp)
            {
                f_logReport(Enum_logState.Error, exp.Message + exp.StackTrace);

            }

            ///delete sms from memory
            ///
            try
            {
                m_semaphoreWriteSMS.WaitOne();

                for (int i = 0; i < obj_deleteResponses.Count; i++)
                {
                    string strCmd = "at+cmgd=" + obj_deleteResponses[i].p_idx.ToString() + ",0\r";
                    cls_Modem.p_getInstance.f_write(strCmd);
                    strCmd = cls_Modem.p_getInstance.f_readFeedback();
                }

                m_semaphoreWriteSMS.Release();

            }
            catch (Exception exp)
            {

                f_logReport(Enum_logState.Error, exp.Message + exp.StackTrace);
            }

            return obj_receivedSMS;

        }



        /// <summary>
        /// Set the modem on PDU mode
        /// </summary>
        public void f_setSMSSettingsPDU()
        {
            string strCmd = "at+cmgf=0\r";
            try
            {
                m_semaphoreWriteSMS.WaitOne();

                cls_Modem.p_getInstance.f_write(strCmd);
                cls_Modem.p_getInstance.f_readFeedback();

                m_semaphoreWriteSMS.Release();

            }
            catch (Exception exp)
            {
                m_semaphoreWriteSMS.Release();
                f_logReport(Enum_logState.Error, exp.Message + exp.StackTrace);
            }
        }


        /// <summary>
        /// Send the SMS in pdu mode
        /// </summary>
        /// <param name="NumberTo"></param>
        /// <param name="strMessage"></param>
        /// <param name="isUnicode"></param>
        /// <returns></returns>
        public bool f_sendSMSPDUMode(string NumberTo, string strMessage, bool isUnicode)
        {
            string strSMSTextEncoded = "", strATCommand = "", strReport = "";
            bool SMSState = false;
            GsmComm.PduConverter.SmsSubmitPdu[] obj_smsList = GsmComm.PduConverter.SmartMessaging.SmartMessageFactory.CreateConcatTextMessage(strMessage, isUnicode, NumberTo);

            try
            {
                m_semaphoreWriteSMS.WaitOne();

                for (int i = 0; i < obj_smsList.Length; i++)
                {

                    strSMSTextEncoded = obj_smsList[i].ToString();

                    strATCommand = "AT+CMGS=" + obj_smsList[i].ActualLength + "\r";

                    cls_Modem.p_getInstance.f_write(strATCommand);
                    cls_Modem.p_getInstance.f_readFeedback();

                    strATCommand = strSMSTextEncoded + Char.ConvertFromUtf32(26);

                    cls_Modem.p_getInstance.f_write(strATCommand);
                    strReport = cls_Modem.p_getInstance.f_readFeedback();

                    SMSState = f_sentSMSOK(strReport);
                    if (SMSState == false)
                    {
                        break;
                    }

                }

                m_semaphoreWriteSMS.Release();
            }
            catch (Exception exp)
            {
                m_semaphoreWriteSMS.Release();

                f_logReport(Enum_logState.Error, exp.Message + exp.StackTrace);
                return false;
            }

            return SMSState;
        }


        /// <summary>
        /// Send the SMS in text mode
        /// </summary>
        /// <param name="NumberTo"></param>
        /// <param name="strMessage"></param>
        /// <param name="isUnicode"></param>
        /// <returns></returns>
        public bool f_sendSMSTextMode(string NumberTo, string strMessage, bool isUnicode)
        {
            string strCmd = "", strReport = "";
            try
            {
                m_semaphoreWriteSMS.WaitOne();

                strCmd = "at+cmgf=1\r";
                cls_Modem.p_getInstance.f_write(strCmd);
                cls_Modem.p_getInstance.f_readFeedback();

                string strSMSTextEncoded = "";
                if (isUnicode)
                {
                    strCmd = "at+cscs=\"UCS2\"\r";
                    cls_Modem.p_getInstance.f_write(strCmd);
                    cls_Modem.p_getInstance.f_readFeedback();

                    strCmd = "at+csmp=17,167,0,8\r";
                    cls_Modem.p_getInstance.f_write(strCmd);
                    cls_Modem.p_getInstance.f_readFeedback();

                    strSMSTextEncoded = f_getSMSTextModeEncoded(strMessage);
                }
                else
                {
                    strSMSTextEncoded = strMessage;
                }

                strCmd = "AT+CMGS=\"" + NumberTo + "\"\r";
                cls_Modem.p_getInstance.f_write(strCmd);
                cls_Modem.p_getInstance.f_readFeedback();


                strCmd = strSMSTextEncoded + "\r" + Char.ConvertFromUtf32(26);
                cls_Modem.p_getInstance.f_write(strCmd);
                strReport = cls_Modem.p_getInstance.f_readFeedback();

                m_semaphoreWriteSMS.Release();

            }
            catch (Exception exp)
            {
                m_semaphoreWriteSMS.Release();
                f_logReport(Enum_logState.Error, exp.Message + exp.StackTrace);
                return false;
            }

            bool sendState = f_sentSMSOK(strReport);
            return sendState;
        }

        /// <summary>
        /// Get the encoded text for send as text mode
        /// </summary>
        /// <param name="strMsg"></param>
        /// <returns></returns>
        private string f_getSMSTextModeEncoded(string strMsg)
        {
            byte[] bufferMsg = Encoding.Unicode.GetBytes(strMsg);

            string[] encodedMsg = new string[bufferMsg.Length];
            string strSMSTextEncoded = "";
            for (int i = 0; i < bufferMsg.Length; i = i + 2)
            {
                strSMSTextEncoded += bufferMsg[i + 1].ToString("X2");
                strSMSTextEncoded += bufferMsg[i].ToString("X2"); ;
            }

            return strSMSTextEncoded;
        }

        /// <summary>
        /// If sending OK
        /// </summary>
        /// <param name="strReport"></param>
        /// <returns></returns>
        private bool f_sentSMSOK(string strReport)
        {
            if (Regex.IsMatch(@strReport, cls_base.PATTERN_SEND_SMS_OK))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// AT-command execute successfully
        /// </summary>
        /// <param name="strReport"></param>
        /// <returns></returns>
        private bool f_commandIsOK(string strReport)
        {
            if (strReport.EndsWith(cls_base.OK_FEEDBACK))
            {
                return true;
            }
            else if (!strReport.EndsWith(cls_base.ERROR_FEEDBACK)
                    || !Regex.IsMatch(strReport, cls_base.PATTERN_ERROR))
            {
                return false;
            }
            else
            {
                return false;
            }
        }


        private void f_logReport(Enum_logState logState, string strLog)
        {
            
        }
        

        

        /// <summary>
        /// To know the count of SMS
        /// </summary>
        /// <param name="strMessage"></param>
        /// <param name="NumberTo"></param>
        /// <param name="isUnicode"></param>
        /// <returns></returns>
        public int f_getSMSCount(string strMessage, string NumberTo, bool isUnicode)
        {
            try
            {
                SmsSubmitPdu[] obj_smsList = SmartMessageFactory.CreateConcatTextMessage(strMessage, isUnicode, NumberTo);
                return obj_smsList.Length;
            }
            catch
            {
                return 1;
            }

        }


        

        public bool f_testConnection(string str_comPort)
        {
            m_semaphoreWriteSMS.WaitOne();
            if (cls_Modem.p_getInstance.f_testConnection(str_comPort))
            {
                m_semaphoreWriteSMS.Release();
                return true;
            }
            m_semaphoreWriteSMS.Release();
            return false;
            
        }



        
    }
}
