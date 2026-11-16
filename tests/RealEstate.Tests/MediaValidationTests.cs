using Xunit;
using RealEstate.Infrastructure;
namespace RealEstate.Tests;
public sealed class MediaValidationTests
{
    [Fact] public void Locally_generated_jpeg_and_mp4_validate_and_truncated_video_is_rejected(){
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../Fixtures"));
        var validator=new MediaContentValidator();var jpeg=File.ReadAllBytes(Path.Combine(root,"synthetic.jpeg"));var mp4=File.ReadAllBytes(Path.Combine(root,"synthetic.mp4"));
        Assert.True(validator.Validate("foto.jpg","image/jpeg",jpeg).IsSuccess);
        Assert.True(validator.Validate("video.mp4","video/mp4",mp4).IsSuccess);
        Assert.False(validator.Validate("video.mp4","video/mp4",mp4[..(mp4.Length/2)]).IsSuccess);
        Array.Fill(mp4,(byte)255,0,4);Assert.False(validator.Validate("video.mp4","video/mp4",mp4).IsSuccess);
    }
    [Fact] public void Image_is_decoded_and_dimensions_crc_extension_and_mime_are_checked(){
        var validator=new MediaContentValidator();var png=MediaTestFiles.Png();Assert.True(validator.Validate("foto.png","image/png",png).IsSuccess);
        Assert.False(validator.Validate("foto.jpg","image/jpeg",png).IsSuccess);Assert.False(validator.Validate("foto.png","image/jpeg",png).IsSuccess);
        Assert.False(validator.Validate("../foto.png","image/png",png).IsSuccess);Assert.False(validator.Validate("foto.svg","image/svg+xml",png).IsSuccess);
        Assert.False(validator.Validate("foto.png","image/png",png[..^5]).IsSuccess);Assert.False(validator.Validate("foto.png","image/png",MediaTestFiles.Png(50000,50000)).IsSuccess);
        png[40]^=1;Assert.False(validator.Validate("foto.png","image/png",png).IsSuccess);
    }
    [Theory]
    [InlineData("fake.jpg","image/jpeg")]
    [InlineData("fake.png","image/png")]
    [InlineData("fake.mp4","video/mp4")]
    public void Renamed_or_truncated_content_is_not_accepted(string name,string mime)
    {
        Assert.False(new MediaContentValidator().Validate(name,mime,"<script>not media</script>"u8.ToArray()).IsSuccess);
    }
}
