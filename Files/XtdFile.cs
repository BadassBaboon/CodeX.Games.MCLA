using System;
using System.Collections.Generic;
using System.IO;
using CodeX.Core.Engine;
using CodeX.Games.MCLA.RPF3;
using CodeX.Games.MCLA.RSC5;

namespace CodeX.Games.MCLA.Files
{
    public class XtdFile(Rpf3FileEntry file) : TexturePack(file)
    {
        public Rsc5TextureDictionary TextureDictionary = null;

        public override void Load(byte[] data)
        {
            if (data != null && data.Length >= 12 && BitConverter.ToUInt32(data, 0) == 0x05435352)
            {
                var payload = new byte[data.Length - 12];
                Buffer.BlockCopy(data, 12, payload, 0, payload.Length);
                data = payload;
            }

            var e = FileInfo as Rpf3ResourceFileEntry;
            var r = new Rsc5DataReader(e, data, Core.Utilities.DataEndianess.BigEndian);

            TextureDictionary = r.ReadBlock<Rsc5TextureDictionary>();
            Textures = [];

            var textures = TextureDictionary?.Textures.Items;
            var hashes = TextureDictionary?.Hashes.Items;

            if (textures != null && hashes != null)
            {
                for (int i = 0; i < textures.Length; i++)
                {
                    var tex = textures[i];
                    var hash = hashes[i];

                    tex.Name ??= hash.ToString();
                    Textures[tex.Name] = tex;
                }
            }
        }

        public override byte[] Save()
        {
            if (TextureDictionary == null)
            {
                return null;
            }

            var writer = new Rsc5DataWriter();
            writer.WriteBlock(TextureDictionary);
            return writer.Build((uint)Rpf3ResourceType.Texture);
        }

        public override void BuildFromTextureList(List<Texture> textures)
        {
            TextureDictionary ??= new Rsc5TextureDictionary();

            var rsc5Textures = new List<Rsc5Texture>();
            if (textures != null)
            {
                foreach (var tex in textures)
                {
                    if (tex is Rsc5Texture rsc5Tex)
                    {
                        rsc5Textures.Add(rsc5Tex);
                    }
                    else if (tex != null)
                    {
                        rsc5Textures.Add(Rsc5Texture.Create(tex));
                    }
                }
            }

            TextureDictionary.Build(rsc5Textures);
            base.BuildFromTextureList(new List<Texture>(rsc5Textures));
        }
    }
}
