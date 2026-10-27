using System;
using System.Collections.Generic;
using System.IO;
using CodeX.Core.Engine;
using CodeX.Core.Utilities;
using CodeX.Games.MCLA.RPF3;
using CodeX.Games.MCLA.RSC5;

namespace CodeX.Games.MCLA.Files
{
    public class XcsTexturePack : TexturePack
    {
        public XcsFile Xcs { get; }

        public XcsTexturePack(XcsFile xcs, GameArchiveFileInfo file = null) : base(file ?? xcs?.FileInfo)
        {
            Xcs = xcs;
        }

        public override byte[] Save()
        {
            return Xcs?.Save();
        }

        public override void BuildFromTextureList(List<Texture> textures)
        {
            Xcs?.BuildFromTextureList(textures);
            base.BuildFromTextureList(textures);
        }
    }

    public class XcsFile : PiecePack
    {
        public GameArchiveFileInfo Entry { get; set; }
        public Rsc5CitySector CitySector { get; private set; }
        public Rsc5TextureDictionary CityTextures { get; private set; }
        public JenkHash Hash { get; }
        public string Name { get; }
        public bool DependenciesLoaded { get; set; }

        public byte[] RawData { get; private set; }
        public int VirtualSize { get; private set; }
        public int PhysicalSize { get; private set; }
        public XcsTexturePack TexturePack { get; private set; }

        public XcsFile() : base(null)
        {
            Entry = null;
            Hash = JenkHash.GenHash("");
            Name = "";
        }

        public XcsFile(GameArchiveFileInfo file) : base(file)
        {
            Entry = file;
            Hash = JenkHash.GenHash(file?.NameLower ?? "");
            Name = file?.NameLower;
        }

        public override void Load(byte[] data)
        {
            if (data == null || data.Length == 0) return;

            var e = FileInfo as Rpf3ResourceFileEntry;
            if (data.Length >= 12)
            {
                uint magic = BufferUtil.ReadUint(data, 0);
                if (magic == 0x05435352u || magic == 0x52534305u)
                {
                    bool isBigEndian = (magic == 0x52534305u);
                    uint type = isBigEndian ? Rpf3Crypto.Swap(BufferUtil.ReadUint(data, 4)) : BufferUtil.ReadUint(data, 4);
                    uint flag = isBigEndian ? Rpf3Crypto.Swap(BufferUtil.ReadUint(data, 8)) : BufferUtil.ReadUint(data, 8);

                    VirtualSize = (int)(flag & 0x7FF) << ((int)((flag >> 11) & 15) + 8);
                    PhysicalSize = (int)((flag >> 15) & 0x7FF) << ((int)((flag >> 26) & 15) + 8);

                    if ((flag & 0x40000000) != 0)
                    {
                        int dLen = VirtualSize + PhysicalSize;
                        if (data.Length >= 20)
                        {
                            int streamLen = (int)Rpf3Crypto.Swap(BufferUtil.ReadUint(data, 16));
                            var compressed = new byte[streamLen];
                            System.Buffer.BlockCopy(data, 20, compressed, 0, Math.Min(streamLen, data.Length - 20));
                            data = Rpf3Crypto.DecompressLZX(compressed, dLen);
                        }
                    }
                    else
                    {
                        int totalSize = VirtualSize + PhysicalSize;
                        int headerSize = (data.Length >= 16 && (data.Length - 16) == totalSize) ? 16 : 12;
                        if (data.Length >= headerSize)
                        {
                            var payload = new byte[data.Length - headerSize];
                            System.Buffer.BlockCopy(data, headerSize, payload, 0, payload.Length);
                            data = payload;
                        }
                    }

                    RawData = (byte[])data.Clone();

                    if (e != null)
                    {
                        e.Flag = flag;
                        e.ResourceType = (Rpf3ResourceType)type;
                        e.IsResource = true;
                    }
                }
                else if (e != null)
                {
                    VirtualSize = e.GetVirtualSize();
                    PhysicalSize = e.GetPhysicalSize();
                    RawData = (byte[])data.Clone();
                }
                else
                {
                    RawData = (byte[])data.Clone();
                }
            }
            else if (e != null)
            {
                VirtualSize = e.GetVirtualSize();
                PhysicalSize = e.GetPhysicalSize();
                RawData = (byte[])data.Clone();
            }
            else
            {
                RawData = (byte[])data.Clone();
            }

            var r = new Rsc5DataReader(data, VirtualSize, PhysicalSize, DataEndianess.BigEndian)
            {
                FileEntry = e,
                Position = Rpf3Crypto.VIRTUAL_BASE
            };

            CitySector = r.ReadBlock<Rsc5CitySector>();
            Pieces = [];

            if (CitySector != null)
            {
                var piece = CitySector.SectorPiece.Item;
                if (piece != null)
                {
                    Piece = piece;
                    Pieces.Add(Path.GetFileNameWithoutExtension(FileInfo?.PathLower ?? "piece"), piece);
                }
            }
        }

        public XcsTexturePack GetTexturePack()
        {
            if (TexturePack != null) return TexturePack;

            TexturePack = new XcsTexturePack(this);
            var textures = new Dictionary<string, Texture>();

            var dict = CitySector?.TextureDictionary.Item?.Dict;
            if (dict != null)
            {
                foreach (var kvp in dict)
                {
                    var tex = kvp.Value;
                    if (tex != null)
                    {
                        var name = tex.Name ?? kvp.Key.ToString();
                        textures[name] = tex;
                    }
                }
            }
            TexturePack.Textures = textures;
            return TexturePack;
        }

        public void BuildFromTextureList(List<Texture> textures)
        {
            if (CitySector == null || CitySector.TextureDictionary.Item == null || textures == null) return;
            var texDict = CitySector.TextureDictionary.Item;

            foreach (var t in textures)
            {
                if (t == null || string.IsNullOrEmpty(t.Name)) continue;

                var cleanName = Path.GetFileNameWithoutExtension(t.Name.Replace(".dds", "").ToLowerInvariant());
                var hashDds = JenkHash.GenHash(cleanName + ".dds");
                var hashPlain = JenkHash.GenHash(cleanName);

                Rsc5Texture existing = null;
                if (texDict.Dict.TryGetValue(hashDds, out existing) || texDict.Dict.TryGetValue(hashPlain, out existing))
                {
                    // Found by hash
                }
                else
                {
                    foreach (var kv in texDict.Dict)
                    {
                        if (string.Equals(kv.Value.Name, cleanName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(kv.Value.Name, t.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            existing = kv.Value;
                            break;
                        }
                    }
                }

                if (existing != null && !ReferenceEquals(existing, t))
                {
                    existing.Data = t.Data;
                    existing.Width = t.Width;
                    existing.Height = t.Height;
                    existing.Format = t.Format;
                    existing.MipLevels = (byte)(t.MipLevels > 0 ? t.MipLevels : 1);
                    existing.IsModified = true;
                }
            }
        }

        public override byte[] Save()
        {
            if (RawData == null || CitySector == null || CitySector.TextureDictionary.Item == null)
            {
                return null;
            }

            var texDict = CitySector.TextureDictionary.Item;
            var workingData = new List<byte>(RawData);
            // anyModified

            foreach (var kvp in texDict.Dict)
            {
                var tex = kvp.Value;
                if (tex == null || !tex.IsModified) continue;
                

                byte[] swizzled = Rpf3Crypto.SwizzleXbox360Data(tex.Data, tex.Width, tex.Height, tex.Format);
                var rsc5Format = Rsc5Texture.ConvertToRsc5Format(tex.Format);

                int d3dOffset = (int)(tex.D3DHeaderPosition & 0x0FFFFFFFu);
                uint targetPhysicalOffset;

                if (swizzled.Length <= tex.Size && tex.DataOffset > 0)
                {
                    // Fits in existing allocated physical slot
                    int rawOffset = (int)(tex.DataOffset & 0x0FFFFFFFu);
                    for (int i = 0; i < swizzled.Length; i++)
                    {
                        workingData[rawOffset + i] = swizzled[i];
                    }
                    for (int i = swizzled.Length; i < tex.Size; i++)
                    {
                        workingData[rawOffset + i] = 0;
                    }
                    targetPhysicalOffset = (uint)(rawOffset - VirtualSize);
                }
                else
                {
                    // Expanded physical data (upscaled texture) appended at the end
                    int align = 4096;
                    int pad = (align - (workingData.Count % align)) % align;
                    for (int p = 0; p < pad; p++) workingData.Add(0xCD);

                    int newPhysicalOffset = workingData.Count - VirtualSize;
                    targetPhysicalOffset = (uint)newPhysicalOffset;
                    workingData.AddRange(swizzled);
                }

                // Update D3D Texture Header (64 bytes in Virtual segment)
                if (d3dOffset > 0 && d3dOffset + 64 <= workingData.Count)
                {
                    // Word 7 (offset 0x1C): pitch & tiled flags
                    uint word7 = ReadUInt32BE(workingData, d3dOffset + 0x1C);
                    word7 = (word7 & ~(0x1FFu << 22)) | (((uint)(tex.Width >> 5) & 0x1FFu) << 22);
                    WriteUInt32BE(workingData, d3dOffset + 0x1C, word7);

                    // Word 8 (offset 0x20): Base Address in 0x60000000 + format in lowest byte
                    uint word8 = (0x60000000u + targetPhysicalOffset) | (uint)rsc5Format;
                    WriteUInt32BE(workingData, d3dOffset + 0x20, word8);

                    // Word 9 (offset 0x24): (Width - 1) | ((Height - 1) << 13)
                    uint word9 = ReadUInt32BE(workingData, d3dOffset + 0x24);
                    word9 = (word9 & ~0x03FFFFFFu) | ((uint)(tex.Width - 1) & 0x1FFFu) | (((uint)(tex.Height - 1) & 0x1FFFu) << 13);
                    WriteUInt32BE(workingData, d3dOffset + 0x24, word9);

                    // Word 11 (offset 0x2C): Mip levels
                    uint word11 = ReadUInt32BE(workingData, d3dOffset + 0x2C);
                    word11 = (word11 & ~(0xFu << 6)) | ((uint)(tex.MipLevels & 0xFu) << 6);
                    WriteUInt32BE(workingData, d3dOffset + 0x2C, word11);
                }

                // Update Rsc5Texture header in Virtual space
                int texOffset = (int)(tex.TexturePosition & 0x0FFFFFFFu);
                if (texOffset > 0 && texOffset + 40 <= workingData.Count)
                {
                    // Width at offset 32 (uint16 big-endian)
                    workingData[texOffset + 32] = (byte)((tex.Width >> 8) & 0xFF);
                    workingData[texOffset + 33] = (byte)(tex.Width & 0xFF);
                    // Height at offset 34 (uint16 big-endian)
                    workingData[texOffset + 34] = (byte)((tex.Height >> 8) & 0xFF);
                    workingData[texOffset + 35] = (byte)(tex.Height & 0xFF);
                    // Stride at offset 36 (uint16 big-endian)
                    ushort stride = (ushort)(tex.Width * (tex.Format == TextureFormat.BC1 ? 2 : 4));
                    workingData[texOffset + 36] = (byte)((stride >> 8) & 0xFF);
                    workingData[texOffset + 37] = (byte)(stride & 0xFF);
                    // MipLevels at offset 39 (byte)
                    workingData[texOffset + 39] = tex.MipLevels;
                }
            }

            int finalPhysicalSize = workingData.Count - VirtualSize;
            Rsc5DataWriter.EncodeSegmentSize((uint)VirtualSize, out uint vMantissa, out uint vShift);
            Rsc5DataWriter.EncodeSegmentSize((uint)finalPhysicalSize, out uint pMantissa, out uint pShift);

            uint flag = (vMantissa & 0x7FFu) | ((vShift & 0xFu) << 11);
            if (finalPhysicalSize > 0)
            {
                flag |= ((pMantissa & 0x7FFu) << 15) | ((pShift & 0xFu) << 26);
            }
            // Uncompressed resource (bit 31 = 1, bit 30 = 0)
            flag = (flag | 0x80000000u) & ~0x40000000u;

            // Pad physical data to encoded page boundary
            uint encodedPhysicalSize = (pMantissa & 0x7FFu) << (int)(pShift + 8);
            while (workingData.Count - VirtualSize < encodedPhysicalSize)
            {
                workingData.Add(0xCD);
            }

            // Build standard 16-byte uncompressed RSC5 file (compatible with LARecomp modloader kRsc5HeaderSize)
            byte[] result = new byte[16 + workingData.Count];
            BufferUtil.WriteUint(result, 0, Rpf3Crypto.Swap(0x05435352u));
            BufferUtil.WriteUint(result, 4, Rpf3Crypto.Swap((uint)Rpf3ResourceType.Generic));
            BufferUtil.WriteUint(result, 8, Rpf3Crypto.Swap(flag));
            BufferUtil.WriteUint(result, 12, 0); // 4-byte padding for 16-byte header
            workingData.CopyTo(result, 16);

            return result;
        }

        private static uint ReadUInt32BE(List<byte> list, int offset)
        {
            return ((uint)list[offset] << 24) | ((uint)list[offset + 1] << 16) | ((uint)list[offset + 2] << 8) | (uint)list[offset + 3];
        }

        private static void WriteUInt32BE(List<byte> list, int offset, uint val)
        {
            list[offset] = (byte)((val >> 24) & 0xFF);
            list[offset + 1] = (byte)((val >> 16) & 0xFF);
            list[offset + 2] = (byte)((val >> 8) & 0xFF);
            list[offset + 3] = (byte)(val & 0xFF);
        }
    }
}
