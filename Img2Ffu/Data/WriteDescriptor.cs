﻿using System;
using System.IO;

namespace Img2Ffu.Data
{
    public class WriteDescriptor
    {
        public BlockDataEntry BlockDataEntry { get; set; }
        public DiskLocation[] DiskLocations { get; set; }

        public byte[] GetResultingBuffer(StoreHeaderVersion storeHeaderVersion)
        {
            using MemoryStream WriteDescriptorStream = new();
            BinaryWriter binaryWriter = new(WriteDescriptorStream);

            BlockDataEntry.LocationCount = (uint)DiskLocations.Length;

            binaryWriter.Write(BlockDataEntry.LocationCount);
            binaryWriter.Write(BlockDataEntry.BlockCount);

            switch (storeHeaderVersion)
            {
                case StoreHeaderVersion.V1_COMPRESSED:
                    binaryWriter.Write((uint)0);
                    break;
            }

            foreach (DiskLocation DiskLocation in DiskLocations)
            {
                binaryWriter.Write(DiskLocation.DiskAccessMethod);
                binaryWriter.Write(DiskLocation.BlockIndex);
            }

            byte[] WriteDescriptorBuffer = new byte[WriteDescriptorStream.Length];
            WriteDescriptorStream.Seek(0, SeekOrigin.Begin);
            WriteDescriptorStream.ReadExactly(WriteDescriptorBuffer, 0, WriteDescriptorBuffer.Length);

            return WriteDescriptorBuffer;
        }
    }
}