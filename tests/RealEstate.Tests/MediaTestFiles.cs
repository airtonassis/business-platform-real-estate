using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
namespace RealEstate.Tests;
internal static class MediaTestFiles
{
    public static byte[] Png(int width=2,int height=2){
        using var output=new MemoryStream();output.Write(new byte[]{137,80,78,71,13,10,26,10});
        void Chunk(string type,byte[] data){var n=new byte[4];BinaryPrimitives.WriteInt32BigEndian(n,data.Length);output.Write(n);var body=Encoding.ASCII.GetBytes(type).Concat(data).ToArray();output.Write(body);uint crc=0xffffffff;foreach(var b in body){crc^=b;for(var i=0;i<8;i++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}BinaryPrimitives.WriteUInt32BigEndian(n,crc^0xffffffff);output.Write(n);}
        var header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(header,width);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height);header[8]=8;header[9]=6;Chunk("IHDR",header);
        using var data=new MemoryStream();using(var z=new ZLibStream(data,CompressionLevel.Fastest,true)){for(var y=0;y<Math.Min(height,2);y++){z.WriteByte(0);for(var x=0;x<Math.Min(width,2);x++)z.Write(new byte[]{0,190,210,255});}}
        Chunk("IDAT",data.ToArray());Chunk("IEND",[]);return output.ToArray();
    }
}
