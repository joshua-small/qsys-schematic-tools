using System.Text;
using System.Text.Json;
using Designer.Modules.SelectionProbe;
int checks=0;
void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
void Reject(byte[] bytes,string message){try{SnapshotPayload.Read(bytes);}catch(Exception e)when(e is InvalidDataException or ArgumentException){checks++;return;}throw new Exception(message);}
if(args.Length!=1)throw new Exception("Pass the directory of the retained native long-text fixture.");
using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(args[0],"fixture.json")));
string bank=fixture.RootElement.GetProperty("bankId").GetString()!,gain=fixture.RootElement.GetProperty("encodedGain").GetString()!,script=fixture.RootElement.GetProperty("encodedScript").GetString()!;
var bytes=File.ReadAllBytes(Path.Combine(args[0],"controlset."+bank+".snapshot.1"));
var first=SnapshotPayload.Read(bytes);var second=SnapshotPayload.Read(File.ReadAllBytes(Path.Combine(args[0],"controlset."+bank+".snapshot.2")));
Check(first.Count==7&&second.Count==7,"Real fixture must decode all seven controls, not a partial subset.");
Check(first[gain+"_gain"].Number==-12&&second[gain+"_gain"].Number==-6,"Wrong saved scalar values.");
Check(first[gain+"_mute"].Number==1&&second[gain+"_mute"].Number==0,"Wrong boolean values.");
Check(first[script+"_code"].Text!.StartsWith("-- Snapshot 1 café λ\n-- ")&&first[script+"_code"].Text!.Length>1600,"UTF-8 or long back-references were damaged.");
Check(second[script+"_code"].Text!.StartsWith("-- Snapshot 2 café λ"),"Text snapshot identity was lost.");
Check(first[script+"_magic_number"].IsText&&first[script+"_magic_number"].Text is null,"Empty text lost its stored status.");
Check(first[script+"_magic_number"].SameValue(new(null,"",true)),"Native null text must match empty current feedback.");
Check(!new SnapshotDatum(null,"0",true).SameValue(new(0,null,false)),"String and numeric values must remain distinct.");
Check(!first.ContainsKey(gain+"_missing"),"Missing control must remain absent.");
Check(!first[gain+"_gain"].SameValue(second[gain+"_gain"])&&first[gain+"_bypass"].SameValue(second[gain+"_bypass"]),"Typed comparison failed.");
Check(SnapshotPayload.Read(File.ReadAllBytes(Path.Combine(args[0],"controlset.global.snapshot.0"))).Count>=20,"Larger native Global payload failed.");
Reject(bytes[..^1],"Truncated trailer accepted.");Reject(bytes[..31],"Tiny file accepted.");
var invalid=(byte[])bytes.Clone();invalid[0]=(byte)'x';Reject(invalid,"Unknown encoding accepted.");
invalid=(byte[])bytes.Clone();invalid[1]=0;Reject(invalid,"Zero-length compression token accepted.");
invalid=(byte[])bytes.Clone();invalid[1]=0x81;invalid[2]=1;Reject(invalid,"Back-reference before beginning accepted.");
invalid=(byte[])bytes.Clone();invalid[^1]=(byte)'?';Reject(invalid,"Malformed trailer accepted.");
Reject(new byte[SnapshotPayload.MaximumFileBytes+1],"Oversized file accepted.");
// Synthetic malformed records check rejection paths; no fixture or native file
// is rewritten. The all-zero trailer intentionally tests shape, not a checksum.
byte[] Frame(byte[] body)
{
    using var stream=new MemoryStream();stream.WriteByte((byte)'q');
    for(int offset=0;offset<body.Length;offset+=127){int n=Math.Min(127,body.Length-offset);stream.WriteByte((byte)n);stream.Write(body,offset,n);}
    stream.Write(Encoding.ASCII.GetBytes(new string('0',32)));return stream.ToArray();
}
byte[] Body(Action<BinaryWriter> records,int count=1)
{
    using var stream=new MemoryStream();using var writer=new BinaryWriter(stream,Encoding.UTF8,true);
    writer.Write((byte)'i');writer.Write(0x0bf7fbd9);writer.Write((byte)'i');writer.Write(count);records(writer);writer.Flush();return stream.ToArray();
}
void Key(BinaryWriter writer){writer.Write((byte)'s');writer.Write(1);writer.Write((byte)'k');}
void Number(BinaryWriter writer){Key(writer);writer.Write((byte)'i');writer.Write(7);writer.Write((byte)'e');writer.Write(1f);}
Reject(Frame(Body(w=>{Key(w);w.Write((byte)'i');w.Write(99);})),"Unknown value type accepted.");
Reject(Frame(Body(w=>{Number(w);Number(w);},2)),"Duplicate control IDs accepted.");
Reject(Frame(Body(w=>{Number(w);w.Write((byte)0);})),"Trailing records accepted.");
Reject(Frame(Body(w=>{Key(w);w.Write((byte)'i');w.Write(4);w.Write((byte)'s');w.Write(1);w.Write((byte)0xff);})),"Invalid UTF-8 accepted.");
Reject(Frame(Body(w=>{},100001)),"Unbounded record count accepted.");
Console.WriteLine($"{checks} snapshot payload and comparison checks passed against the native fixture.");
