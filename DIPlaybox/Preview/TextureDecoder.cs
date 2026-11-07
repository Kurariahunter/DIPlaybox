using System;
using System.IO;
using System.Buffers.Binary;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace DIPlaybox.Preview;
// BC1/BC3 decoding shared with the project's native texture preview research.
public static class TextureDecoder
{
    sealed record Result(int Width,int Height,byte[] Bgra);
    public sealed record Decoded(BitmapSource Image,string Format);
    public static Decoded Decode(byte[] bytes)
    {
        if(bytes.Length>64*1024*1024)throw new InvalidDataException("Texture preview input limit is 64 MiB.");
        if(bytes.Length>=4&&System.Text.Encoding.ASCII.GetString(bytes,0,4)=="Gfx2")
        {
            var decoded=Gx2ExternalPreview.Convert(bytes);
            var gx=BitmapSource.Create(decoded.Width,decoded.Height,96,96,PixelFormats.Bgra32,null,decoded.Bgra,decoded.Width*4);
            gx.Freeze();return new(gx,"Wii U GX2 BC1/BC3 / decoded top mip");
        }
        if(bytes.Length>=128 && System.Text.Encoding.ASCII.GetString(bytes,0,4)=="DDS ")
        {
            if(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4,4))!=124)throw new InvalidDataException("Invalid DDS header size.");
            int height=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12,4)),width=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16,4));
            if(width<1||height<1||width>8192||height>8192||(long)width*height>16777216)throw new InvalidDataException("Texture dimensions exceed preview limits.");
            string code=System.Text.Encoding.ASCII.GetString(bytes,84,4);
            uint flags=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(80,4));
            if((flags&4)==0&&(flags&0x40)!=0)
            {
                uint bits=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(88,4));
                uint red=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(92,4)),green=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(96,4)),blue=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(100,4)),alpha=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(104,4));
                bool bgra=red==0xff0000&&green==0xff00&&blue==0xff;
                bool rgba=red==0xff&&green==0xff00&&blue==0xff0000;
                if(bits!=32||(!bgra&&!rgba)||((flags&1)!=0&&alpha!=0xff000000))throw new NotSupportedException("Uncompressed DDS channel masks are not supported yet.");
                int stride=checked(width*4);uint headerFlags=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8,4));
                if((headerFlags&8)!=0){uint pitch=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20,4));if(pitch<stride||pitch>int.MaxValue)throw new InvalidDataException("Invalid DDS row pitch.");stride=(int)pitch;}
                if(128L+(long)(height-1)*stride+width*4L>bytes.Length)throw new InvalidDataException("Uncompressed DDS image is truncated.");
                byte[] pixels=new byte[checked(width*height*4)];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    int source=checked(128+y*stride+x*4),target=(y*width+x)*4;
                    pixels[target]=bytes[source+(bgra?0:2)];pixels[target+1]=bytes[source+1];pixels[target+2]=bytes[source+(bgra?2:0)];pixels[target+3]=(flags&1)!=0?bytes[source+3]:(byte)255;
                }
                var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);bitmap.Freeze();return new(bitmap,(bgra?"BGRA32":"RGBA32")+" / uncompressed top mip");
            }
            if(code!="DXT1"&&code!="DXT5")throw new NotSupportedException("DDS "+code+" is not supported yet; preview supports DXT1/BC1 and DXT5/BC3.");
            var result=DecodeLinearBc(bytes.AsSpan(128),width,height,code=="DXT5");
            var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,result.Bgra,width*4);image.Freeze();return new(image,code+" / top mip");
        }
        if(bytes.Length>=8&&bytes[0]==137&&bytes[1]==80&&bytes[2]==78&&bytes[3]==71)
        {
            using var stream=new MemoryStream(bytes,false);var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();
            if((long)image.PixelWidth*image.PixelHeight>16777216)throw new InvalidDataException("PNG exceeds preview limits.");image.Freeze();return new(image,"PNG");
        }
        throw new NotSupportedException("Unrecognized texture container. Original extraction remains available.");
    }
    static Result DecodeLinearBc(ReadOnlySpan<byte> data,int width,int height,bool bc3)
    {
        int bx=(width+3)/4,by=(height+3)/4,blockBytes=bc3?16:8,n=checked(bx*by*blockBytes);
        if(data.Length<n)throw new InvalidDataException("BC image data truncated.");
        byte[] pixels=new byte[checked(width*height*4)];
        Span<byte> alpha=stackalloc byte[8]; Span<byte> colors=stackalloc byte[16];
        for(int yb=0;yb<by;yb++)for(int xb=0;xb<bx;xb++)
        {
            var block=data.Slice((yb*bx+xb)*blockBytes,blockBytes);
            var colorBlock=block.Slice(bc3?8:0,8);
            ushort c0=BinaryPrimitives.ReadUInt16LittleEndian(colorBlock),c1=BinaryPrimitives.ReadUInt16LittleEndian(colorBlock.Slice(2));
            uint bits=BinaryPrimitives.ReadUInt32LittleEndian(colorBlock.Slice(4));
            ulong alphaBits=0;
            if(bc3)
            {
                alpha[0]=block[0];alpha[1]=block[1];
                if(alpha[0]>alpha[1])
                    for(int i=2;i<8;i++)alpha[i]=(byte)(((8-i)*alpha[0]+(i-1)*alpha[1])/7);
                else
                {
                    for(int i=2;i<6;i++)alpha[i]=(byte)(((6-i)*alpha[0]+(i-1)*alpha[1])/5);
                    alpha[6]=0;alpha[7]=255;
                }
                for(int i=0;i<6;i++)alphaBits|=(ulong)block[2+i]<<(8*i);
            }

            Color565(c0,colors);Color565(c1,colors.Slice(4));
            for(int c=0;c<3;c++)
            {
                colors[8+c]=(byte)((bc3||c0>c1)?(2*colors[c]+colors[4+c])/3:(colors[c]+colors[4+c])/2);
                colors[12+c]=(byte)((bc3||c0>c1)?(colors[c]+2*colors[4+c])/3:0);
            }
            colors[3]=colors[7]=colors[11]=255;colors[15]=(byte)((bc3||c0>c1)?255:0);
            for(int py=0;py<4;py++)for(int px=0;px<4;px++)
            {
                int x=xb*4+px,y=yb*4+py;if(x>=width||y>=height)continue;
                int src=(int)((bits>>(2*(py*4+px)))&3)*4;
                int target=(y*width+x)*4;
                colors.Slice(src,4).CopyTo(pixels.AsSpan(target,4));
                if(bc3)pixels[target+3]=alpha[(int)((alphaBits>>(3*(py*4+px)))&7)];
            }
        }
        return new Result(width,height,pixels);
    }

    static void Color565(ushort c,Span<byte> p)
    {
        p[0]=(byte)(((c&31)*255+15)/31);
        p[1]=(byte)((((c>>5)&63)*255+31)/63);
        p[2]=(byte)((((c>>11)&31)*255+15)/31);
    }
}
