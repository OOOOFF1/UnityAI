using System;
using System.Globalization;//处理时间格式化，解决不同电脑地区语言导致时间字符串格式错乱
using System.Security.Cryptography;//密码学，HMACSHA256哈希签名算法，讯飞要求的签名算法
using System.Text;//Encoding.UTF8字节编码转换

//用途：讯飞语音听写 STT、语音合成 TTS，使用 websocket 长连接，不能直接用 apiKey 密钥连地址，必须按照讯飞文档算法，把时间、host、密钥做 HMAC?SHA256 签名，拼接出一个带鉴权参数的 wss 地址，用这个地址去建立 WebSocket 连接
/// <summary>
/// 讯飞 WebSocket 鉴权工具,STT / TTS 通用,静态工具类
/// </summary>
public static class XunfeiAuth
{
    // STT 鉴权,例如：wss://iat-api.xfyun.cn/v2/iat
    public static string CreateAuthUrl(string host,string path,string apiKey,string apiSecret)
    {
        return CreateWebSocketAuthUrl(host,path,apiKey,apiSecret);
    }

    // TTS 鉴权,例如： wss://tts-api.xfyun.cn/v2/tts
    public static string CreateTTSAuthUrl(string host,string path,string apiKey,string apiSecret)
    {
        return CreateWebSocketAuthUrl(host,path,apiKey,apiSecret);
    }

    // 通用讯飞 WebSocket HMAC-SHA256 鉴权
    private static string CreateWebSocketAuthUrl(string host,string path,string apiKey,string apiSecret)
    {
        if (string.IsNullOrWhiteSpace(host))
            //throw new ArgumentException：参数不对直接报错，控制台红色警告，不让代码继续往下执行，避免生成错误 url
            throw new ArgumentException("XunfeiAuth：host 不能为空");

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("XunfeiAuth：path 不能为空");

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("XunfeiAuth：ApiKey 不能为空");

        if (string.IsNullOrWhiteSpace(apiSecret))
            throw new ArgumentException("XunfeiAuth：ApiSecret 不能为空");

        //当前 GMT 时间
        //DateTime.UtcNow：世界标准时间，不是电脑本地时区时间
        //.ToString("r")：.NET 内置 RFC1123 时间格式，输出样例：Wed, 01 Oct 2025 08:20:10 GMT，讯飞接口强制要求这个时间字符串格式
        //CultureInfo.InvariantCulture：无视操作系统的语言地区，强制英文时间格式
        string date = DateTime.UtcNow.ToString("r",CultureInfo.InvariantCulture);

        //生成签名原文
        //格式必须严格和讯飞文档一字不差，空格、换行 \n 不能多不能少，错一个字符签名就完全不一样，返回 401 未授权
        string signatureOrigin ="host: " + host + "\n" +"date: " + date + "\n" +"GET " + path + " HTTP/1.1";

        //HMAC-SHA256
        //拿上面一大段原文 + apiSecret（密钥），算出 base64 格式签名
        string signature = HmacSha256Base64(signatureOrigin,apiSecret);

        //authorization 原文
        string authorizationOrigin ="api_key=\"" + apiKey + "\"" +", algorithm=\"hmac-sha256\"" +", headers=\"host date request-line\"" +", signature=\"" + signature + "\"";

        //Base64
        //讯飞协议规定：把上面 authorizationOrigin 整串 UTF8 转字节流，再做 Base64，放到 url 参数 authorization
        string authorization =Convert.ToBase64String(Encoding.UTF8.GetBytes(authorizationOrigin));

        //最终 WebSocket URL
        //Uri.EscapeDataString()：URL 编码
        string authUrl ="wss://" +host +path +"?authorization=" +Uri.EscapeDataString(authorization) +"&date=" +Uri.EscapeDataString(date) +"&host=" +Uri.EscapeDataString(host);

        return authUrl;
    }

    //HMAC-SHA256 + Base64
    private static string HmacSha256Base64(string text,string secret)
    {
        //把签名原文 text、密钥 secret 全部转为 UTF?8 字节数组
        byte[] key =Encoding.UTF8.GetBytes(secret);
        byte[] data =Encoding.UTF8.GetBytes(text);

        //new HMACSHA256(key)：使用 apiSecret 作为加密密钥
        //using：自动释放加密类的资源，避免内存泄漏
        using (HMACSHA256 hmac =new HMACSHA256(key))
        {
            //ComputeHash(data)：执行 HMAC?SHA256 运算，得到哈希字节
            byte[] hash =hmac.ComputeHash(data);

            //Convert.ToBase64String(hash)：把哈希字节转 base64 字符串，就是 signature 签名
            return Convert.ToBase64String(hash);
        }
    }
}
