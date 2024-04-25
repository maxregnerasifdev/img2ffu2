/*
    Copyright (c) 2019-2024, Gustave Monce - gus33000.me - @gus33000
    Released under the MIT License. See LICENSE.md for details.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using CommandLine;

namespace Img2Ffu
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(RunWithOptions);
        }

        private static void RunWithOptions(Options o)
        {
            DisplayStartupMessage();
            ProcessImageFile(o);
        }

        private static void DisplayStartupMessage()
        {
            Logging.Log("img2ffu - Converts raw image (img) files into full flash update (FFU) files");
            Logging.Log("Copyright (c) 2019-2024, Gustave Monce - gus33000.me - @gus33000");
            Logging.Log("Released under the MIT license at github.com/WOA-Project/img2ffu");
            Logging.Log("");
        }

        private static void ProcessImageFile(Options o)
        {
            string filePath = EnsureFilePath(o.ExcludedPartitionNamesFilePath);

            if (!File.Exists(filePath))
            {
                Logging.Log("Error: Provisioning partition file not found.", Logging.LoggingLevel.Error);
                Environment.Exit(1);
            }

            try
            {
                GenerateFFU(o.InputFile, o.FFUFile, o.PlatformID.Split(';'), o.SectorSize, o.BlockSize,
                    o.AntiTheftVersion, o.OperatingSystemVersion, File.ReadAllLines(filePath),
                    o.MaximumNumberOfBlankBlocksAllowed, FlashUpdateVersion.V2);
            }
            catch (Exception ex)
            {
                Logging.Log($"Error: {ex.Message}", Logging.LoggingLevel.Error);
                Environment.Exit(1);
            }
        }

        private static string EnsureFilePath(string path)
        {
            if (!File.Exists(path))
            {
                return Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), path);
            }
            return path;
        }

        private static void GenerateFFU(string inputFile, string ffuFile, string[] platformIDs, uint sectorSize,
            uint blockSize, string antiTheftVersion, string osVersion, string[] excludedPartitionNames,
            uint maxBlankBlocks, FlashUpdateVersion flashVersion)
        {
            if (File.Exists(ffuFile))
            {
                Logging.Log("Error: FFU file already exists.", Logging.LoggingLevel.Error);
                return;
            }

            (var minSectorCount, var partitions, var storeHeaderBuffer, var writeDescriptorBuffer, var blockPayloads, var flashParts, var inputDisk) =
                GenerateStore(inputFile, platformIDs, sectorSize, blockSize, excludedPartitionNames, maxBlankBlocks, flashVersion);

            Logging.Log("Generating FFU...");
            WriteFFU(ffuFile, storeHeaderBuffer, writeDescriptorBuffer, blockPayloads, flashParts, blockSize);

            inputDisk?.Dispose();
        }

        private static (uint, List<GPT.Partition>, byte[], byte[], KeyValuePair<ByteArrayKey, BlockPayload>[], FlashPart[], VirtualDisk) GenerateStore(
            string inputFile, string[] platformIDs, uint sectorSize, uint blockSize, string[] excludedPartitionNames,
            uint maxBlankBlocks, FlashUpdateVersion flashVersion)
        {
            Logging.Log("Opening input file...");

            var inputStream = GetInputStream(inputFile);
            var (flashParts, partitions) = ImageSplitter.GetImageSlices(inputStream, blockSize, excludedPartitionNames, sectorSize);
            var blockPayloads = BlockPayloadsGenerator.GetOptimizedPayloads(flashParts, blockSize, maxBlankBlocks);

            var writeDescriptorBuffer = GetWriteDescriptorsBuffer(blockPayloads, flashVersion);
            var storeHeaderBuffer = GenerateStoreHeader(platformIDs, blockSize, blockPayloads.Length, writeDescriptorBuffer.Length);

            uint minSectorCount = (uint)(inputStream.Length / sectorSize);
            return (minSectorCount, partitions, storeHeaderBuffer, writeDescriptorBuffer, blockPayloads, flashParts, inputStream as VirtualDisk);
        }

        private static Stream GetInputStream(string inputFile)
        {
            if (File.Exists(inputFile))
            {
                return new FileStream(inputFile, FileMode.Open);
            }

            Logging.Log("Error: Unknown input specified", Logging.LoggingLevel.Error);
            throw new FileNotFoundException("Input file not found.");
        }

        private static byte[] GenerateStoreHeader(string[] platformIDs, uint blockSize, long blockPayloadCount, long writeDescriptorLength)
        {
            var store = new StoreHeader
            {
                PlatformIds = platformIDs,
                BlockSize = blockSize,
                WriteDescriptorCount = (uint)blockPayloadCount,
                WriteDescriptorLength = (uint)writeDescriptorLength
            };
            return store.GetResultingBuffer(FlashUpdateVersion.V2, FlashUpdateType.Full, CompressionAlgorithm.None);
        }

        private static void WriteFFU(string ffuFile, byte[] storeHeaderBuffer, byte[] writeDescriptorBuffer,
            KeyValuePair<ByteArrayKey, BlockPayload>[] blockPayloads, FlashPart[] flashParts, uint blockSize)
        {
            using var ffuFileStream = new FileStream(ffuFile, FileMode.CreateNew);
            ffuFileStream.Write(storeHeaderBuffer, 0, storeHeaderBuffer.Length);
            ffuFileStream.Write(writeDescriptorBuffer, 0, writeDescriptorBuffer.Length);
            Logging.Log("FFU File written successfully.");
        }

        private static byte[] GetWriteDescriptorsBuffer(KeyValuePair<ByteArrayKey, BlockPayload>[] payloads, FlashUpdateVersion version)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            foreach (var payload in payloads)
            {
                byte[] buffer = payload.Value.WriteDescriptor.GetResultingBuffer(version);
                writer.Write(buffer);
            }
            return stream.ToArray();
        }
    }
}
