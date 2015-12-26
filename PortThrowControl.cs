using UnityEngine;
using System.Collections;
//Other libraries
using System;
using System.Threading;
using System.Collections.Generic;
using System.ComponentModel;
//串口命名空间
using System.Text.RegularExpressions;
using System.Text;
using System.IO.Ports;

public class PortThrowControl : MonoBehaviour
{

    List<byte> liststr;//在ListByte中读取数据，用于做数据处理
    List<byte> ListByte;//存放读取的串口数据
    private Thread tPort;//读取串口数据，处理数据的线程
    bool isStartThread;//控制FixedUpdate里面的两个线程是否调用（当准备调用串口的Close方法时设置为false）
    byte[] strOutPool = new byte[6];
    SerialPort spstart;
    void Start()
    {
        print(123);
        liststr = new List<byte>();
        ListByte = new List<byte>();

        isStartThread = true;
        spstart = new SerialPort("COM4", 115200, Parity.None, 8, StopBits.One);
        spstart.ReadTimeout = 10;

        try
        {
            spstart.Open();
            spstart.DiscardInBuffer();
        }
        catch (Exception e)
        {
            Debug.Log(e.ToString());
        }
        tPort = new Thread(ReceiveData);
        tPort.Start();


        InvokeRepeating("SendTest", 1f, 0.05f);

    }

    void FixedUpdate()
    {

        if (isStartThread)
        {
            if (!tPort.IsAlive)
            {
                tPort = new Thread(ReceiveData);
                tPort.Start();

            }


        }

        
        if (Input.GetKeyDown(KeyCode.W))
        {
            SendTest();
            //SendData(SendDataDeal(0x4c, 0x01, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 0xc4));
        }
    }

    int tickcount = 0;
    float spantime = 0;


    #region #发和处理数据#
    //***********************发送数据********************************//
    private void SendData(byte[] data)
    {
        try
        {


            if (spstart.IsOpen)
            {
                spstart.Write(data, 0, data.Length);
            }

        }
        catch (Exception e)
        {
            Debug.Log(e.ToString());
        }

    }

    byte[] SendDataDeal(byte pSTX, byte pCMD, byte[] pDAT, byte pEDX)
    {
        byte[] data = new byte[pDAT.Length + 6];

        data[1] = (byte)(pDAT.Length + 1);//长度

        byte pBCC = CalculateBCC(data[1], pCMD, pDAT);//BCC


        byte pRDM = (byte)new System.Random().Next(0,256);//RDM
        data[data.Length - 3] = pRDM;

        byte[] eDAT = new byte[pDAT.Length];
        pDAT.CopyTo(eDAT, 0);
        XOREncrypt(ref pCMD, ref eDAT, ref pBCC, pRDM);

        data[0] = pSTX;//包头
        data[data.Length - 1] = pEDX;//包尾


        //加密后把数据放入data中
        data[2] = pCMD;//CMD

        for (int i = 0; i < eDAT.Length; i++)//DAT
        {
            data[3 + i] = eDAT[i];
        }

        data[data.Length - 2] = pBCC;//

        print("send");

        return data;
    }

    void XOREncrypt(ref byte pCMD, ref byte[] pDAT, ref byte pBCC, byte pRDM)//把密文都与RDM异或一次加密(目前只有CMD,DAT,BCC需要加密)
    {

        pCMD ^= pRDM;
        for (int i = 0; i < pDAT.Length; i++)
        {
            pDAT[i] ^= pRDM;
        }
        pBCC ^= pRDM;

    }
    byte CalculateBCC(byte pLEN, byte pCMD, byte[] pDAT)
    {
        byte sum = 0;
        sum += pLEN;
        sum += pCMD;
        for (int i = 0; i < pDAT.Length; i++)
        {
            sum += pDAT[i];
        }

        return (byte)(sum % 256);

    }
    #endregion

    #region #收和处理数据#
    //***********************处理收到的数据********************************//

    private void ReceiveData()
    {
        int bytecount=0;
        try
        {
            Byte[] buf = new Byte[100];
            if (spstart.IsOpen)
            {
                tickcount++;
                bytecount=spstart.Read(buf, 0, 100);
            }
            if (bytecount == 0)
            {
                return;
            }
            if (buf != null)
            {

                for (int i = 0; i < bytecount; i++)
                {
                    
                    ListByte.Add(buf[i]);
                    ReceiveDataDeal();
                    ListByte.Remove(ListByte[0]);
                }

                


                
                
            }


        }
        catch (Exception e)
        {
            Debug.Log(e.ToString());
            spantime = 0;
        }
        //Thread.Sleep(2);


    }



    public bool isGetHead = false;//是否获取包头，如果拿了包头，要检测到包尾才算收到完整的数据
    public bool isGetingData = false;//是否正在获取data，获取中就置为true
    public uint datacount = 0;//计算CMD+DATA读取位置
    public byte LEN;
    public byte CMD = 0;
    public byte[] DAT = new byte[1];
    public byte RDM;
    public byte BCC = 0;
    private void ReceiveDataDeal()//处理数据,根据包头包尾判断指令开始结束
    {

        liststr.Add(ListByte[0]);

        if ((ListByte[0] == 0x4c ) && !isGetHead)//收到包头
        {
            
            LEN = 0;//长度先为0，以免冲突
            isGetHead = true;
            return;
        }
        if (isGetHead)//读到了包头，才开始读下面的数据
        {
            if (LEN == 0)//接受数据长度,长度一定不为0，用这个判断
            {

                LEN = ListByte[0];
                LEN = 106;//这个长度一定是106
                isGetingData = true;//开始获取数据
                return;
            }

            if (isGetingData && datacount == 0)//读取CMD
            {
                CMD = ListByte[0];
                DAT = new byte[LEN - 1];
                datacount++;
                return;
            }
            if (isGetingData && datacount > 0)//读取DAT
            {
                DAT[datacount - 1] = ListByte[0];
                datacount++;
                if (datacount == LEN)
                {
                    isGetingData = false;
                }
                return;
            }

            if (!isGetingData && datacount == (LEN))//读取RDM(判断 读取完数据)
            {
                RDM = ListByte[0];
                datacount++;
                return;
            }
            if (!isGetingData && datacount == (LEN + 1))//读取BCC(判断 读取完数据的+1)
            {
                BCC = ListByte[0];
                datacount++;
                return;
            }

            if (ListByte[0] == 0xc4 )//获取到包尾
            {

                XORDecode(ref CMD, ref DAT, ref BCC, RDM);//解码


                if (CheckBCC(LEN, CMD, DAT, BCC))//校验位检测
                {
                    print("commandfinish");

                    DataRead(CMD, DAT);

                    string str = "";
                    foreach (byte b in DAT)
                    {
                        str += b.ToString("X2") + " ";
                    }
                    Debug.LogError(str);
                    //Debug.LogError(DAT[0]+" "+DAT[1]+" "+
                    //    DAT[9].ToString("D3") + " " + DAT[10].ToString("D3") + " " + DAT[11].ToString("D3") + " " + DAT[12].ToString("D3") + " " + DAT[13].ToString("D3") + " " + DAT[14].ToString("D3"));
                }
                else
                {
                    print("commandBCCerror");
                }
            }
            else
            {
                print("commandLASTerror");
            }

            isGetHead = false;
            isGetingData = false;
            datacount = 0;
            liststr.Clear();
        }


    }

    void DataRead(byte pCMD,byte[] pDAT)//对接受到的DAT和CMD进行解读
    {
        

    }
    bool CompareByte(byte[] b1, byte[] b2)
    { 
        if(b1.Length!=b2.Length)
        {
            return false;
        }
        for (int i = 0; i < b1.Length; i++)
        {
            if (b1[i] != b2[i])
            {
                return false;
            }
        }
        return true;
    }


    void XORDecode(ref byte pCMD, ref byte[] pDAT, ref byte pBCC, byte pRDM)//把密文都与RDM异或一次解码，变回原文(目前只有CMD,DAT,BCC需要解码)
    {

        pCMD ^= pRDM;

        for (int i = 0; i < pDAT.Length; i++)
        {
            pDAT[i] ^= pRDM;
        }

        pBCC ^= pRDM;

    }
    bool CheckBCC(byte pLEN, byte pCMD, byte[] pDAT, byte pBCC)
    {
        byte sum = 0;
        sum += pLEN;
        sum += pCMD;
        for (int i = 0; i < pDAT.Length; i++)
        {
            sum += pDAT[i];
        }

        if (sum % 256 == pBCC)
        {
            return true;
        }

        return false;
    }

    #endregion

    #region #处理陀螺仪和转轮数据#
    void SendTest()
    {
        print(21231);
        try
        {
            byte[] data = new byte[] {0x4c,0x05, 0x01, 0x00 ,0x00 ,0x00 ,0x00 ,0x00 ,0x06 ,0xc4 };
            if (spstart.IsOpen)
            {
                spstart.Write(data, 0, data.Length);
            }

        }
        catch (Exception e)
        {
            Debug.Log(e.ToString());
        }
    }

    #endregion
    void ClosePort()//该方法为关闭串口的方法，当程序退出或是离开该页面或是想停止串口时调用。
    {

        isStartThread = false;//停止掉FixedUpdate里面的两个线程的调用
        tPort.Abort();

        spstart.Close();
    }
    


    void OnApplicationQuit()
    {
        Thread close = new Thread(ClosePort);
        close.Start();
    }


}
