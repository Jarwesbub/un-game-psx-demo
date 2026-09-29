using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using UnityEngine;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// Uploads a .ps-exe to a PS1 running Unirom 8 via serial.
    /// Implements the NOTPSXSerial / Unirom protocol:
    ///   Challenge/Response handshake → header → metadata → chunked data with checksums.
    /// Reference: https://github.com/JonathanDotCel/NOTPSXSerial
    /// </summary>
    public static class UniromUploader
    {
        // Protocol constants
        private const string CHALLENGE_SEND_EXE = "SEXE";
        private const string RESPONSE_OK = "OKAY";
        private const int CHUNK_SIZE = 2048;
        private const int HEADER_SIZE = 0x800; // 2048
        private const int SERIAL_TIMEOUT_MS = 5000;

        // Protocol version — negotiated during handshake
        private static int _protocolVersion = 1;

        /// <summary>
        /// Uploads a .ps-exe file to the PS1 via serial.
        /// The PS1 must be at the Unirom shell prompt.
        /// </summary>
        public static bool UploadExe(string portName, int baudRate, string exePath, Action<string> log)
        {
            var port = DoUpload(portName, baudRate, exePath, log, installDebugHooks: false);
            if (port == null) return false;
            try { port.Close(); } catch { }
            port.Dispose();
            return true;
        }

        /// <summary>
    /// Uploads a .ps-exe with Unirom debug hooks installed, using SBIN+JUMP
    /// instead of SEXE to avoid BIOS Exec() clobbering the debug handler.
    ///
    /// Flow: DEBG (install kernel-resident debug hooks) → SBIN (raw binary to address)
    /// → JUMP (start execution at entry point). This bypasses BIOS Exec() entirely,
    /// so the exception vector table patched by DEBG survives into the running program.
    ///
    /// Returns the open SerialPort for the caller to use for PCDrv monitoring.
    /// The caller takes ownership of the returned port.
    /// </summary>
    public static SerialPort UploadExeForPCdrv(string portName, int baudRate, string exePath, Action<string> log)
        {
            return DoUploadSBIN(portName, baudRate, exePath, log);
        }

        /// <summary>
        /// Core SEXE upload implementation. Opens port, optionally sends DEBG, does SEXE upload.
        /// Used by UploadExe() for simple uploads without PCDrv.
        /// Returns the open SerialPort (caller must close/dispose when done).
        /// Returns null on failure.
        /// </summary>
        private static SerialPort DoUpload(string portName, int baudRate, string exePath, Action<string> log, bool installDebugHooks)
        {
            if (!File.Exists(exePath))
            {
                log?.Invoke($"File not found: {exePath}");
                return null;
            }

            byte[] exeData = File.ReadAllBytes(exePath);
            log?.Invoke($"Uploading {Path.GetFileName(exePath)} ({exeData.Length} bytes)");

            // Pad to 2048-byte sector boundary (required by Unirom)
            int mod = exeData.Length % CHUNK_SIZE;
            if (mod != 0)
            {
                int paddingRequired = CHUNK_SIZE - mod;
                byte[] padded = new byte[exeData.Length + paddingRequired];
                Buffer.BlockCopy(exeData, 0, padded, 0, exeData.Length);
                exeData = padded;
                log?.Invoke($"Padded to {exeData.Length} bytes (2048-byte boundary)");
            }

            _protocolVersion = 1;
            SerialPort port = null;

            try
            {
                port = new SerialPort(portName, baudRate)
                {
                    ReadTimeout = SERIAL_TIMEOUT_MS,
                    WriteTimeout = SERIAL_TIMEOUT_MS,
                    StopBits = StopBits.Two,
                    Parity = Parity.None,
                    DataBits = 8,
                    Handshake = Handshake.None,
                    DtrEnable = true,
                    RtsEnable = true
                };
                port.Open();

                // Drain any leftover bytes in the buffer
                while (port.BytesToRead > 0)
                    port.ReadByte();

                // ── Step 0 (PCDrv only): Install debug hooks while Unirom is still in command mode ──
                if (installDebugHooks)
                {
                    log?.Invoke("Installing debug hooks (DEBG)...");
                    if (!ChallengeResponse(port, "DEBG", "OKAY", log))
                    {
                        log?.Invoke("WARNING: DEBG failed. Is Unirom at the shell? PCDrv may not work.");
                    }
                    else
                    {
                        log?.Invoke("Debug hooks installed.");
                    }

                    Thread.Sleep(100);
                    while (port.BytesToRead > 0)
                        port.ReadByte();
                }

                // ── Step 1: Challenge/Response handshake ──
                log?.Invoke("Sending SEXE challenge...");
                if (!ChallengeResponse(port, CHALLENGE_SEND_EXE, RESPONSE_OK, log))
                {
                    log?.Invoke("No response from Unirom. Is the PS1 at the Unirom shell?");
                    port.Close(); port.Dispose();
                    return null;
                }
                log?.Invoke($"Unirom responded (protocol V{_protocolVersion}). Starting transfer...");

                // ── Step 2: Calculate checksum (skip first 0x800 header sector) ──
                uint checksum = CalculateChecksum(exeData, skipFirstSector: true);

                // ── Step 3: Send the 2048-byte header sector ──
                port.Write(exeData, 0, HEADER_SIZE);

                // ── Step 4: Send metadata ──
                port.Write(exeData, 0x10, 4);  // Jump/PC address
                port.Write(exeData, 0x18, 4);  // Base/write address
                port.Write(BitConverter.GetBytes(exeData.Length - HEADER_SIZE), 0, 4);  // Data length
                port.Write(BitConverter.GetBytes(checksum), 0, 4);  // Checksum

                // ── Step 5: Send data chunks (skip first sector) ──
                if (!WriteChunked(port, exeData, skipFirstSector: true, log))
                {
                    log?.Invoke("Data transfer failed.");
                    port.Close(); port.Dispose();
                    return null;
                }

                log?.Invoke("Upload complete. Exe executing on PS1.");
                return port;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Upload failed: {ex.Message}");
                if (port != null && port.IsOpen)
                {
                    try { port.Close(); } catch { }
                }
                port?.Dispose();
                return null;
            }
        }

        /// <summary>
        /// Uploads a .ps-exe using DEBG + SBIN + JUMP to preserve debug hooks.
        ///
        /// Unlike SEXE which calls BIOS Exec() (reinitializing the exception vector table
        /// and destroying DEBG's kernel-resident debug handler), SBIN writes raw bytes
        /// directly to the target address and JUMP starts execution without touching
        /// the BIOS. This preserves the break-instruction handler that PCDrv depends on.
        ///
        /// Protocol:
        ///   1. DEBG → OKAY: Install kernel-resident SIO debug stub
        ///   2. SBIN → OKAY: addr(4 LE) + len(4 LE) + checksum(4 LE) + raw program data
        ///   3. JUMP → OKAY: addr(4 LE) — jump to entry point
        /// </summary>
        private static SerialPort DoUploadSBIN(string portName, int baudRate, string exePath, Action<string> log)
        {
            if (!File.Exists(exePath))
            {
                log?.Invoke($"File not found: {exePath}");
                return null;
            }

            byte[] exeData = File.ReadAllBytes(exePath);
            log?.Invoke($"Uploading {Path.GetFileName(exePath)} ({exeData.Length} bytes) via SBIN+JUMP");

            // Validate this is a PS-X EXE
            if (exeData.Length < HEADER_SIZE + 4)
            {
                log?.Invoke("File too small to be a valid PS-X EXE.");
                return null;
            }
            string magic = Encoding.ASCII.GetString(exeData, 0, 8);
            if (!magic.StartsWith("PS-X EXE"))
            {
                log?.Invoke($"Not a PS-X EXE (magic: '{magic}')");
                return null;
            }

            // Parse header
            uint entryPoint = BitConverter.ToUInt32(exeData, 0x10);  // PC / jump address
            uint destAddr = BitConverter.ToUInt32(exeData, 0x18);    // Copy destination
            uint textSize = BitConverter.ToUInt32(exeData, 0x1C);    // Text section size

            log?.Invoke($"PS-X EXE: entry=0x{entryPoint:X8}, dest=0x{destAddr:X8}, textSz=0x{textSize:X}");

            // Extract program data (everything after the 2048-byte header)
            int progDataLen = exeData.Length - HEADER_SIZE;
            byte[] progData = new byte[progDataLen];
            Buffer.BlockCopy(exeData, HEADER_SIZE, progData, 0, progDataLen);

            // Pad program data to 2048-byte boundary (required by Unirom chunked transfer)
            int mod = progData.Length % CHUNK_SIZE;
            if (mod != 0)
            {
                int paddingRequired = CHUNK_SIZE - mod;
                byte[] padded = new byte[progData.Length + paddingRequired];
                Buffer.BlockCopy(progData, 0, padded, 0, progData.Length);
                progData = padded;
                log?.Invoke($"Program data padded to {progData.Length} bytes");
            }

            _protocolVersion = 1;
            SerialPort port = null;

            try
            {
                port = new SerialPort(portName, baudRate)
                {
                    ReadTimeout = SERIAL_TIMEOUT_MS,
                    WriteTimeout = SERIAL_TIMEOUT_MS,
                    StopBits = StopBits.Two,
                    Parity = Parity.None,
                    DataBits = 8,
                    Handshake = Handshake.None,
                    DtrEnable = true,
                    RtsEnable = true
                };
                port.Open();

                // Drain any leftover bytes
                while (port.BytesToRead > 0)
                    port.ReadByte();

                // ── Step 1: DEBG — Install kernel-resident debug hooks ──
                log?.Invoke("Installing debug hooks (DEBG)...");
                if (!ChallengeResponse(port, "DEBG", "OKAY", log))
                {
                    log?.Invoke("DEBG failed. Is Unirom at the shell?");
                    port.Close(); port.Dispose();
                    return null;
                }
                log?.Invoke("Debug hooks installed.");

                // Drain + settle — Unirom may send extra bytes after DEBG
                Thread.Sleep(100);
                while (port.BytesToRead > 0)
                    port.ReadByte();

                // ── Step 2: SBIN — Upload raw program data to target address ──
                log?.Invoke($"Sending SBIN to 0x{destAddr:X8} ({progData.Length} bytes)...");
                if (!ChallengeResponse(port, "SBIN", "OKAY", log))
                {
                    log?.Invoke("SBIN failed. Unirom may not support this command.");
                    port.Close(); port.Dispose();
                    return null;
                }

                // SBIN metadata: address(4) + length(4) + checksum(4)
                uint checksum = CalculateChecksum(progData, skipFirstSector: false);
                port.Write(BitConverter.GetBytes(destAddr), 0, 4);
                port.Write(BitConverter.GetBytes(progData.Length), 0, 4);
                port.Write(BitConverter.GetBytes(checksum), 0, 4);

                log?.Invoke($"SBIN metadata sent (checksum=0x{checksum:X8}). Sending data...");

                // Send program data chunks
                if (!WriteChunked(port, progData, skipFirstSector: false, log))
                {
                    log?.Invoke("SBIN data transfer failed.");
                    port.Close(); port.Dispose();
                    return null;
                }
                log?.Invoke("SBIN upload complete.");

                // Drain any residual
                Thread.Sleep(100);
                while (port.BytesToRead > 0)
                    port.ReadByte();

                // ── Step 3: JUMP — Start execution at entry point ──
                log?.Invoke($"Sending JUMP to 0x{entryPoint:X8}...");
                if (!ChallengeResponse(port, "JUMP", "OKAY", log))
                {
                    log?.Invoke("JUMP failed.");
                    port.Close(); port.Dispose();
                    return null;
                }
                // JUMP payload: just the address (4 bytes LE)
                port.Write(BitConverter.GetBytes(entryPoint), 0, 4);

                log?.Invoke("JUMP sent. Exe now running (debug hooks preserved).");
                return port;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Upload failed: {ex.Message}");
                if (port != null && port.IsOpen)
                {
                    try { port.Close(); } catch { }
                }
                port?.Dispose();
                return null;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Challenge / Response with protocol negotiation
        // ═══════════════════════════════════════════════════════════════

        private static bool ChallengeResponse(SerialPort port, string challenge, string expectedResponse, Action<string> log)
        {
            // Send the challenge
            byte[] challengeBytes = Encoding.ASCII.GetBytes(challenge);
            port.Write(challengeBytes, 0, challengeBytes.Length);
            Thread.Sleep(50);

            // Wait for the response with protocol negotiation
            return WaitResponse(port, expectedResponse, log);
        }

        private static bool WaitResponse(SerialPort port, string expected, Action<string> log, int timeoutMs = 10000)
        {
            string buffer = "";
            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);

            while (DateTime.Now < deadline)
            {
                if (port.BytesToRead > 0)
                {
                    buffer += (char)port.ReadByte();

                    // Keep buffer at 4 chars max (rolling window)
                    if (buffer.Length > 4)
                        buffer = buffer.Substring(buffer.Length - 4);

                    // Protocol V3 upgrade (DJB2 checksums)
                    // Always respond — Unirom re-offers V2/V3 for each command,
                    // and our protocolVersion may already be >1 from a prior DEBG exchange.
                    if (buffer == "OKV3")
                    {
                        log?.Invoke("Upgraded to protocol V3");
                        byte[] upv3 = Encoding.ASCII.GetBytes("UPV3");
                        port.Write(upv3, 0, upv3.Length);
                        _protocolVersion = 3;
                        buffer = "";
                        continue;
                    }

                    // Protocol V2 upgrade (per-chunk checksums)
                    if (buffer == "OKV2")
                    {
                        log?.Invoke("Upgraded to protocol V2");
                        byte[] upv2 = Encoding.ASCII.GetBytes("UPV2");
                        port.Write(upv2, 0, upv2.Length);
                        if (_protocolVersion < 2) _protocolVersion = 2;
                        buffer = "";
                        continue;
                    }

                    // Unsupported in debug mode
                    if (buffer == "UNSP")
                    {
                        log?.Invoke("Command not supported while Unirom is in debug mode.");
                        return false;
                    }

                    // Got the expected response
                    if (buffer == expected)
                        return true;
                }
                else
                {
                    Thread.Sleep(1);
                }
            }

            return false;
        }

        // ═══════════════════════════════════════════════════════════════
        // Chunked data transfer with per-chunk checksum verification
        // ═══════════════════════════════════════════════════════════════

        private static bool WriteChunked(SerialPort port, byte[] data, bool skipFirstSector, Action<string> log)
        {
            int start = skipFirstSector ? CHUNK_SIZE : 0;
            int totalDataBytes = data.Length - start;
            int numChunks = (totalDataBytes + CHUNK_SIZE - 1) / CHUNK_SIZE;
            int chunkIndex = 0;

            for (int offset = start; offset < data.Length; )
            {
                // Determine chunk size (last chunk may be smaller)
                int thisChunk = Math.Min(CHUNK_SIZE, data.Length - offset);

                // Calculate per-chunk checksum (simple byte sum for V2, also works for V1)
                ulong chunkChecksum = 0;
                for (int j = 0; j < thisChunk; j++)
                    chunkChecksum += data[offset + j];

                // Send the chunk
                port.Write(data, offset, thisChunk);

                // Wait for bytes to drain
                while (port.BytesToWrite > 0)
                    Thread.Sleep(0);

                chunkIndex++;

                // Progress report every 10 chunks or on last chunk
                if (chunkIndex % 10 == 0 || offset + thisChunk >= data.Length)
                {
                    int sent = offset + thisChunk - start;
                    int pct = totalDataBytes > 0 ? sent * 100 / totalDataBytes : 100;
                    log?.Invoke($"Upload: {pct}% ({sent}/{totalDataBytes})");
                }

                // Protocol V2/V3: per-chunk checksum verification
                if (_protocolVersion >= 2)
                {
                    if (!HandleChunkAck(port, chunkChecksum, data, offset, thisChunk, log, out bool retry))
                    {
                        return false;
                    }
                    if (retry)
                        continue; // Don't advance offset — resend this chunk
                }

                offset += thisChunk;
            }

            return true;
        }

        /// <summary>
        /// Handles the per-chunk CHEK/MORE/ERR! exchange for protocol V2+.
        /// </summary>
        private static bool HandleChunkAck(SerialPort port, ulong chunkChecksum, byte[] data, int offset, int chunkSize, Action<string> log, out bool retry)
        {
            retry = false;

            // Wait for "CHEK" request from Unirom
            string cmdBuffer = "";
            DateTime deadline = DateTime.Now.AddMilliseconds(SERIAL_TIMEOUT_MS);

            while (DateTime.Now < deadline)
            {
                if (port.BytesToRead > 0)
                {
                    cmdBuffer += (char)port.ReadByte();
                    if (cmdBuffer.Length > 4)
                        cmdBuffer = cmdBuffer.Substring(cmdBuffer.Length - 4);

                    if (cmdBuffer == "CHEK")
                        break;
                }
                else
                {
                    Thread.Sleep(1);
                }
            }

            if (cmdBuffer != "CHEK")
            {
                log?.Invoke("Timeout waiting for CHEK from Unirom");
                return false;
            }

            // Send the chunk checksum (4 bytes, little-endian)
            port.Write(BitConverter.GetBytes((uint)chunkChecksum), 0, 4);
            Thread.Sleep(1);

            // Wait for MORE (ok) or ERR! (resend)
            cmdBuffer = "";
            deadline = DateTime.Now.AddMilliseconds(SERIAL_TIMEOUT_MS);

            while (DateTime.Now < deadline)
            {
                if (port.BytesToRead > 0)
                {
                    cmdBuffer += (char)port.ReadByte();
                    if (cmdBuffer.Length > 4)
                        cmdBuffer = cmdBuffer.Substring(cmdBuffer.Length - 4);

                    if (cmdBuffer == "MORE")
                        return true;

                    if (cmdBuffer == "ERR!")
                    {
                        log?.Invoke("Checksum error — retrying chunk...");
                        retry = true;
                        return true;
                    }
                }
                else
                {
                    Thread.Sleep(1);
                }
            }

            log?.Invoke("Timeout waiting for MORE/ERR! from Unirom");
            return false;
        }

        // ═══════════════════════════════════════════════════════════════
        // Checksum calculation
        // ═══════════════════════════════════════════════════════════════

        private static uint CalculateChecksum(byte[] data, bool skipFirstSector)
        {
            int start = skipFirstSector ? HEADER_SIZE : 0;

            if (_protocolVersion == 3)
            {
                // DJB2 hash
                uint hash = 5381;
                for (int i = start; i < data.Length; i++)
                    hash = ((hash << 5) + hash) ^ data[i];
                return hash;
            }
            else
            {
                // Simple byte sum
                uint sum = 0;
                for (int i = start; i < data.Length; i++)
                    sum += data[i];
                return sum;
            }
        }
    }
}
