using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using RealEstate.Domain;
using StbImageSharp;
namespace RealEstate.Infrastructure;
public sealed class MediaContentValidator:IMediaContentValidator
{
    public Outcome<MediaFile> Validate(string name,string mime,byte[] bytes)
    {
        try{
            if(!MediaRules.NameValid(name)||bytes.Length==0)return Outcome<MediaFile>.Fail("Media.InvalidContent");
            var ext=Path.GetExtension(name).ToLowerInvariant();
            var valid=ext switch{
                ".jpg" or ".jpeg"=>mime=="image/jpeg"&&bytes.Length<=MediaRules.ImageLimit&&bytes.Length>4&&bytes[0]==255&&bytes[1]==216&&bytes[^2]==255&&bytes[^1]==217&&Image(bytes),
                ".png"=>mime=="image/png"&&bytes.Length<=MediaRules.ImageLimit&&Png(bytes)&&Image(bytes),
                ".mp4"=>mime=="video/mp4"&&bytes.Length<=MediaRules.VideoLimit&&Mp4(bytes),_=>false};
            return valid?new(new MediaFile(name,mime,mime.StartsWith("image/",StringComparison.Ordinal)?"image":"video",bytes,Convert.ToHexString(SHA256.HashData(bytes)))):Outcome<MediaFile>.Fail("Media.InvalidContent");
        }catch(Exception e) when(e is not OutOfMemoryException and not OperationCanceledException){return Outcome<MediaFile>.Fail("Media.InvalidContent");}
    }
    private static bool Image(byte[] bytes){using var stream=new MemoryStream(bytes,false);var info=ImageInfo.FromStream(stream);if(info is null||info.Value.Width<=0||info.Value.Height<=0||info.Value.Width>12000||info.Value.Height>12000||(long)info.Value.Width*info.Value.Height>20000000)return false;var decoded=ImageResult.FromMemory(bytes,ColorComponents.RedGreenBlueAlpha);return decoded.Width==info.Value.Width&&decoded.Height==info.Value.Height&&decoded.Data.Length==(long)decoded.Width*decoded.Height*4;}
    private static uint U32(byte[] b,int n)=>BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(n,4));
    private static bool Png(byte[] b){
        if(b.Length<45||!b.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))return false;
        var p=8;var first=true;var data=false;
        while(p+12<=b.Length){var size=U32(b,p);if(size>int.MaxValue||size>b.Length-p-12)return false;var type=Encoding.ASCII.GetString(b,p+4,4);var length=(int)size;
            uint crc=0xffffffff;for(var i=p+4;i<p+8+length;i++){crc^=b[i];for(var j=0;j<8;j++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}
            if((crc^0xffffffff)!=U32(b,p+8+length))return false;
            if(first&&(type!="IHDR"||size!=13))return false;first=false;
            if(type=="acTL")return false; // animated PNG is outside the admitted formats
            if(type=="IDAT")data=true;p+=12+length;
            if(type=="IEND")return data&&size==0&&p==b.Length;
        }return false;
    }
    private readonly record struct Box(string Type,int Start,int End);
    private static List<Box> Boxes(byte[] b,int from,int end){
        var boxes=new List<Box>();var p=from;
        while(p<end){if(end-p<8||boxes.Count>10000)throw new InvalidDataException();ulong size=U32(b,p);var header=8;
            if(size==1){if(end-p<16)throw new InvalidDataException();size=BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(p+8,8));header=16;}
            if(size==0)size=(ulong)(end-p);
            if(size<(ulong)header||size>(ulong)(end-p))throw new InvalidDataException();boxes.Add(new(Encoding.ASCII.GetString(b,p+4,4),p+header,p+(int)size));p+=(int)size;
        }return boxes;
    }
    private static bool Mp4(byte[] b){
        var top=Boxes(b,0,b.Length);var ftyp=top.SingleOrDefault(x=>x.Type=="ftyp");
        if(ftyp.End-ftyp.Start<8||!top.Any(x=>x.Type=="mdat"&&x.End-x.Start>=8))return false;
        var brands=new HashSet<string>{"isom","iso2","iso4","iso5","iso6","mp41","mp42","avc1","dash"};
        if(!brands.Contains(Encoding.ASCII.GetString(b,ftyp.Start,4)))return false;
        var moov=top.SingleOrDefault(x=>x.Type=="moov");if(moov.End==0)return false;
        foreach(var trak in Boxes(b,moov.Start,moov.End).Where(x=>x.Type=="trak")){
            var track=Boxes(b,trak.Start,trak.End);var mdia=track.SingleOrDefault(x=>x.Type=="mdia");if(mdia.End==0)continue;
            var media=Boxes(b,mdia.Start,mdia.End);var handler=media.SingleOrDefault(x=>x.Type=="hdlr");
            if(handler.End-handler.Start<12||Encoding.ASCII.GetString(b,handler.Start+8,4)!="vide")continue;
            var minf=media.SingleOrDefault(x=>x.Type=="minf");if(minf.End==0)return false;
            var stbl=Boxes(b,minf.Start,minf.End).SingleOrDefault(x=>x.Type=="stbl");if(stbl.End==0)return false;
            var stsd=Boxes(b,stbl.Start,stbl.End).SingleOrDefault(x=>x.Type=="stsd");if(stsd.End-stsd.Start<8||U32(b,stsd.Start+4)!=1)return false;
            var sample=Boxes(b,stsd.Start+8,stsd.End).Single();if(sample.Type is not ("avc1" or "avc3")||sample.End-sample.Start<78)return false;
            var width=BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(sample.Start+24,2));var height=BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(sample.Start+26,2));
            if(width==0||height==0||(long)width*height>20000000)return false;
            var avcc=Boxes(b,sample.Start+78,sample.End).SingleOrDefault(x=>x.Type=="avcC");
            if(avcc.End-avcc.Start<7||b[avcc.Start]!=1)return false;
            var p=avcc.Start+6;var sps=b[avcc.Start+5]&31;if(sps==0)return false;
            for(var i=0;i<sps;i++){if(p+2>avcc.End)return false;var n=BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(p,2));p+=2;if(n==0||p+n>avcc.End||(b[p]&31)!=7)return false;p+=n;}
            if(p>=avcc.End)return false;var pps=b[p++];if(pps==0)return false;
            for(var i=0;i<pps;i++){if(p+2>avcc.End)return false;var n=BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(p,2));p+=2;if(n==0||p+n>avcc.End||(b[p]&31)!=8)return false;p+=n;}
            var tkhd=track.SingleOrDefault(x=>x.Type=="tkhd");if(tkhd.End-tkhd.Start<4)return false;var offset=b[tkhd.Start]==1?20:12;if(tkhd.End-tkhd.Start<offset+4)return false;
            return SamplesFit(b,top,moov,stbl,U32(b,tkhd.Start+offset));
        }return false;
    }
    private static bool SamplesFit(byte[] b,List<Box> top,Box moov,Box stbl,uint videoTrack){
        long samples=0,bytes=0;var mdat=top.Where(x=>x.Type=="mdat").Sum(x=>(long)x.End-x.Start);
        var size=Boxes(b,stbl.Start,stbl.End).SingleOrDefault(x=>x.Type=="stsz");
        if(size.End!=0){if(size.End-size.Start<12)return false;var fixedSize=U32(b,size.Start+4);var count=U32(b,size.Start+8);if(count>1000000)return false;samples+=count;
            if(fixedSize>0)bytes+=(long)fixedSize*count;
            else{if((long)size.End-size.Start<12L+4L*count)return false;for(var i=0;i<count;i++)bytes+=U32(b,size.Start+12+i*4);}}
        var defaults=new Dictionary<uint,uint>();var mvex=Boxes(b,moov.Start,moov.End).SingleOrDefault(x=>x.Type=="mvex");
        if(mvex.End!=0)foreach(var trex in Boxes(b,mvex.Start,mvex.End).Where(x=>x.Type=="trex")){if(trex.End-trex.Start<24)return false;defaults[U32(b,trex.Start+4)]=U32(b,trex.Start+16);}
        foreach(var moof in top.Where(x=>x.Type=="moof"))foreach(var traf in Boxes(b,moof.Start,moof.End).Where(x=>x.Type=="traf")){
            var children=Boxes(b,traf.Start,traf.End);var tfhd=children.SingleOrDefault(x=>x.Type=="tfhd");if(tfhd.End-tfhd.Start<8)return false;
            if(U32(b,tfhd.Start+4)!=videoTrack)continue;
            var flags=U32(b,tfhd.Start)&0xffffff;var p=tfhd.Start+8;uint fallback=defaults.GetValueOrDefault(U32(b,tfhd.Start+4));
            if((flags&1)!=0)p+=8;if((flags&2)!=0)p+=4;if((flags&8)!=0)p+=4;if((flags&16)!=0){if(p+4>tfhd.End)return false;fallback=U32(b,p);}
            foreach(var trun in children.Where(x=>x.Type=="trun")){if(trun.End-trun.Start<8)return false;var trunFlags=U32(b,trun.Start)&0xffffff;var count=U32(b,trun.Start+4);if(count>1000000)return false;samples+=count;p=trun.Start+8;
                if((trunFlags&1)!=0)p+=4;if((trunFlags&4)!=0)p+=4;
                for(var i=0;i<count;i++){if((trunFlags&0x100)!=0)p+=4;uint sample=fallback;if((trunFlags&0x200)!=0){if(p+4>trun.End)return false;sample=U32(b,p);p+=4;}if(sample==0)return false;bytes+=sample;if((trunFlags&0x400)!=0)p+=4;if((trunFlags&0x800)!=0)p+=4;if(p>trun.End)return false;}
                if(p!=trun.End)return false;
            }
        }return samples>0&&bytes>0&&bytes<=mdat;
    }
}
