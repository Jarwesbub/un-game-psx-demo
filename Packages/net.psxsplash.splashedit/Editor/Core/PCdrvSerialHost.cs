using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// PCdrv Host — serves files to a PS1 over serial (Unirom/NOTPSXSerial protocol).
    /// 
    /// PCdrv uses MIPS `break` instructions to request file I/O from the host.
    /// For real hardware running Unirom, we must:
    ///   1. Enter debug mode (DEBG/OKAY) — installs Unirom's kernel-resident SIO handler
    ///   2. Continue execution (CONT/OKAY)
    ///   3. Monitor serial for escape sequences: 0x00 followed by 'p' = PCDrv command
    ///   4. Handle file operations (init, open, read, close, seek, etc.)
    /// 
    /// Without entering debug mode first, `break` instructions cause an unhandled
    /// "BP break (0x9)" crash because no handler is registered.
    /// 
    /// Protocol based on NOTPSXSerial: https://github.com/JonathanDotCel/NOTPSXSerial
    /// </summary>
    public class PCdrvSerialHost : IDisposable
    {
        private SerialPort _port;
        private CancellationTokenSource _cts;
        private Task _listenTask;
        private readonly string _portName;
        private readonly int _baudRate;
        private readonly string _baseDir;
        private readonly Action<string> _log;
        private readonly Action<string> _psxLog;

        // File handle table (1-indexed, handles are not recycled)
        private readonly List<PCFile> _files = new List<PCFile>();

        private class PCFile
        {
            public string Name;
            public FileStream Stream;
            public int Handle;
            public bool Closed;
            public FileAccess Mode;
        }

        // Protocol escape char — PCDrv commands are prefixed with 0x00 + 'p'
        private const byte ESCAPE_CHAR = 0x00;

        // PCDrv function codes (from Unirom kernel)
        private const int FUNC_INIT   = 0x101;
        private const int FUNC_CREAT  = 0x102;
        private const int FUNC_OPEN   = 0x103;
        private const int FUNC_CLOSE  = 0x104;
        private const int FUNC_READ   = 0x105;
        private const int FUNC_WRITE  = 0x106;
        private const int FUNC_SEEK   = 0x107;

        public bool IsRunning => _listenTask != null && !_listenTask.IsCompleted;
        public bool HasError { get; private set; }

        public PCdrvSerialHost(string portName, int baudRate, string baseDir, Action<string> log, Action<string> psxLog = null)
        {
            _portName = portName;
            _baudRate = baudRate;
            _baseDir = baseDir;
            _log = log;
            _psxLog = psxLog;
        }

        /// <summary>
        /// Opens a new serial port and begins the monitor/PCdrv loop.
        /// Note: DEBG must have been sent BEFORE the exe was uploaded (via UniromUploader.UploadExeForPCdrv).
        /// Use the Start(SerialPort) overload to pass an already-open port from the uploader.
        /// </summary>
        public void Start()
        {
            if (IsRunning) return;

            _port = new SerialPort(_portName, _baudRate)
            {
                ReadTimeout = 5000,
                WriteTimeout = 5000,
                StopBits = StopBits.Two,
                Parity = Parity.None,
                DataBits = 8,
                Handshake = Handshake.None,
                DtrEnable = true,
                RtsEnable = true
            };

            _port.Open();
            _log?.Invoke($"PCdrv host: opened {_portName} @ {_baudRate}");
            _log?.Invoke($"PCdrv host: serving files from {_baseDir}");

            StartMonitorLoop();
        }

        /// <summary>
        /// Starts the PCDrv monitor loop on an already-open serial port.
        /// Use this after UniromUploader.UploadExeForPCdrv() which sends DEBG → SEXE
        /// and returns the open port. The debug hooks are already installed and the
        /// exe is already running — we just need to listen for escape sequences.
        /// </summary>
        public void Start(SerialPort openPort)
        {
            if (IsRunning) return;

            _port = openPort;
            _log?.Invoke($"PCdrv host: serving files from {_baseDir}");
            _log?.Invoke("PCdrv host: monitoring for PCDrv requests...");

            StartMonitorLoop();
        }

        private void StartMonitorLoop()
        {
            _cts = new CancellationTokenSource();
            _listenTask = Task.Run(() => MonitorLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listenTask?.Wait(2000); } catch { }

            foreach (var f in _files)
            {
                if (!f.Closed && f.Stream != null)
                {
                    try { f.Stream.Close(); f.Stream.Dispose(); } catch { }
                }
            }
            _files.Clear();

            if (_port != null && _port.IsOpen)
            {
                try { _port.Close(); } catch { }
            }

            _port?.Dispose();
            _port = null;
            _log?.Invoke("PCdrv host stopped.");
        }

        public void Dispose() => Stop();

        // ═══════════════════════════════════════════════════════════════
        // Monitor loop — reads serial byte-by-byte looking for escape sequences
        // Matches NOTPSXSerial's Bridge.MonitorSerial()
        // ═══════════════════════════════════════════════════════════════

        private void MonitorLoop(CancellationToken ct)
        {
            bool lastByteWasEscape = false;
            var textBuffer = new StringBuilder();
            int totalBytesReceived = 0;
            int consecutiveErrors = 0;
            DateTime lastLogTime = DateTime.Now;

            _log?.Invoke("PCdrv monitor: waiting for data from PS1...");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_port.BytesToRead == 0)
                    {
                        // Flush any accumulated text output periodically
                        if (textBuffer.Length > 0 && (DateTime.Now - lastLogTime).TotalMilliseconds > 100)
                        {
                            EmitPsxLine(textBuffer.ToString());
                            textBuffer.Clear();
                            lastLogTime = DateTime.Now;
                        }
                        Thread.Sleep(1);
                        continue;
                    }

                    int b = _port.ReadByte();
                    consecutiveErrors = 0;
                    totalBytesReceived++;

                    // Log first bytes received to help diagnose protocol issues
                    if (totalBytesReceived <= 32)
                    {
                        _log?.Invoke($"PCdrv monitor: byte #{totalBytesReceived} = 0x{b:X2} ('{(b >= 0x20 && b < 0x7F ? (char)b : '.')}')");
                    }
                    else if (totalBytesReceived == 33)
                    {
                        _log?.Invoke("PCdrv monitor: (suppressing per-byte logging, check PS1> lines for output)");
                    }

                    if (lastByteWasEscape)
                    {
                        lastByteWasEscape = false;

                        // Flush any text before handling escape
                        if (textBuffer.Length > 0)
                        {
                            EmitPsxLine(textBuffer.ToString());
                            textBuffer.Clear();
                        }

                        if (b == ESCAPE_CHAR)
                        {
                            // Double escape = literal 0x00 in output, ignore
                            continue;
                        }

                        if (b == 'p')
                        {
                            // PCDrv command incoming
                            _log?.Invoke("PCdrv monitor: got escape+p → PCDrv command!");
                            HandlePCDrvCommand(ct);
                        }
                        else
                        {
                            // Unknown escape sequence — log it
                            _log?.Invoke($"PCdrv monitor: unknown escape seq: 0x00 + 0x{b:X2} ('{(b >= 0x20 && b < 0x7F ? (char)b : '.')}')");
                        }

                        continue;
                    }

                    if (b == ESCAPE_CHAR)
                    {
                        lastByteWasEscape = true;
                        continue;
                    }

                    // Regular byte — this is printf output from the PS1
                    if (b == '\n' || b == '\r')
                    {
                        if (textBuffer.Length > 0)
                        {
                            EmitPsxLine(textBuffer.ToString());
                            textBuffer.Clear();
                            lastLogTime = DateTime.Now;
                        }
                    }
                    else if (b >= 0x20 && b < 0x7F)
                    {
                        textBuffer.Append((char)b);
                        // Flush long lines immediately
                        if (textBuffer.Length >= 200)
                        {
                            EmitPsxLine(textBuffer.ToString());
                            textBuffer.Clear();
                            lastLogTime = DateTime.Now;
                        }
                    }
                    // else: non-printable byte that's not escape, skip
                }
                catch (TimeoutException) { }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) break;
                    consecutiveErrors++;
                    _log?.Invoke($"PCdrv monitor error: {ex.Message}");
                    if (consecutiveErrors >= 3)
                    {
                        _log?.Invoke("PCdrv host: too many errors, connection lost. Stopping.");
                        HasError = true;
                        break;
                    }
                    Thread.Sleep(100); // Back off before retry
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // PCDrv command dispatcher
        // Matches NOTPSXSerial's PCDrv.ReadCommand()
        // ═══════════════════════════════════════════════════════════════

        private void HandlePCDrvCommand(CancellationToken ct)
        {
            int funcCode = ReadInt32(ct);

            switch (funcCode)
            {
                case FUNC_INIT:  HandleInit(); break;
                case FUNC_CREAT: HandleCreate(ct); break;
                case FUNC_OPEN:  HandleOpen(ct); break;
                case FUNC_CLOSE: HandleClose(ct); break;
                case FUNC_READ:  HandleRead(ct); break;
                case FUNC_WRITE: HandleWrite(ct); break;
                case FUNC_SEEK:  HandleSeek(ct); break;
                default:
                    _log?.Invoke($"PCdrv: unknown function 0x{funcCode:X}");
                    break;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Individual PCDrv handlers — match NOTPSXSerial's PCDrv.cs
        // ═══════════════════════════════════════════════════════════════

        private void HandleInit()
        {
            _log?.Invoke("PCdrv: INIT");
            SendString("OKAY");
            _port.Write(new byte[] { 0 }, 0, 1); // null terminator expected by Unirom
        }

        private void HandleOpen(CancellationToken ct)
        {
            // Unirom sends: we respond OKAY first, then read filename + mode
            SendString("OKAY");

            string filename = ReadNullTermString(ct);
            int modeParam = ReadInt32(ct);

            // Log raw bytes for debugging garbled filenames
            _log?.Invoke($"PCdrv: OPEN \"{filename}\" mode={modeParam} (len={filename.Length}, hex={BitConverter.ToString(System.Text.Encoding.ASCII.GetBytes(filename))})");

            // Check if already open
            var existing = FindOpenFile(filename);
            if (existing != null)
            {
                _log?.Invoke($"PCdrv: already open, handle={existing.Handle}");
                SendString("OKAY");
                WriteInt32(existing.Handle);
                return;
            }

            string fullPath;
            try
            {
                fullPath = ResolvePath(filename);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"PCdrv: invalid filename \"{filename}\": {ex.Message}");
                SendString("NOPE");
                return;
            }

            if (!File.Exists(fullPath))
            {
                _log?.Invoke($"PCdrv: file not found: {fullPath}");
                SendString("NOPE");
                return;
            }

            try
            {
                var fs = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                int handle = NextHandle();
                _files.Add(new PCFile { Name = filename, Stream = fs, Handle = handle, Closed = false, Mode = FileAccess.ReadWrite });

                SendString("OKAY");
                WriteInt32(handle);
                _log?.Invoke($"PCdrv: opened handle={handle}");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"PCdrv: open failed: {ex.Message}");
                SendString("NOPE");
            }
        }

        private void HandleCreate(CancellationToken ct)
        {
            SendString("OKAY");

            string filename = ReadNullTermString(ct);
            int parameters = ReadInt32(ct);

            _log?.Invoke($"PCdrv: CREAT \"{filename}\" params={parameters}");

            var existing = FindOpenFile(filename);
            if (existing != null)
            {
                SendString("OKAY");
                WriteInt32(existing.Handle);
                return;
            }

            string fullPath;
            try { fullPath = ResolvePath(filename); }
            catch (Exception ex)
            {
                _log?.Invoke($"PCdrv: invalid filename \"{filename}\": {ex.Message}");
                SendString("NOPE");
                return;
            }

            try
            {
                // Create or truncate the file
                if (!File.Exists(fullPath))
                {
                    var temp = File.Create(fullPath);
                    temp.Flush(); temp.Close(); temp.Dispose();
                }

                var fs = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                int handle = NextHandle();
                _files.Add(new PCFile { Name = filename, Stream = fs, Handle = handle, Closed = false, Mode = FileAccess.ReadWrite });

                SendString("OKAY");
                WriteInt32(handle);
                _log?.Invoke($"PCdrv: created handle={handle}");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"PCdrv: create failed: {ex.Message}");
                SendString("NOPE");
            }
        }

        private void HandleClose(CancellationToken ct)
        {
            // Unirom sends: we respond OKAY first, then read handle + 2 unused params
            SendString("OKAY");

            int handle = ReadInt32(ct);
            int _unused1 = ReadInt32(ct);
            int _unused2 = ReadInt32(ct);

            _log?.Invoke($"PCdrv: CLOSE handle={handle}");

            var f = FindOpenFile(handle);
            if (f == null)
            {
                // No such file — "great success" per NOTPSXSerial
                SendString("OKAY");
                WriteInt32(0);
                return;
            }

            try
            {
                f.Stream.Close();
                f.Stream.Dispose();
                f.Closed = true;
                SendString("OKAY");
                WriteInt32(handle);
                _log?.Invoke($"PCdrv: closed handle={handle}");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"PCdrv: close error: {ex.Message}");
                SendString("NOPE");
            }
        }

        private void HandleRead(CancellationToken ct)
        {
            // Unirom sends: we respond OKAY first, then read handle + len + memaddr
            SendString("OKAY");

            int handle = ReadInt32(ct);
            int length = ReadInt32(ct);
            int memAddr = ReadInt32(ct); // for debugging only

            _log?.Invoke($"PCdrv: READ handle={handle} len=0x{length:X} memAddr=0x{memAddr:X}");

            var f = FindOpenFile(handle);
            if (f == null)
            {
                _log?.Invoke($"PCdrv: no file with handle {handle}");
                SendString("NOPE");
                return;
            }

            try
            {
                byte[] data = new byte[length];
                int bytesRead = f.Stream.Read(data, 0, length);

                SendString("OKAY");
                WriteInt32(data.Length);

                // Checksum (simple byte sum, forced V3 = true per NOTPSXSerial)
                uint checksum = CalculateChecksum(data);
                WriteUInt32(checksum);

                // Send data using chunked writer (with per-chunk ack for V2+)
                WriteDataChunked(data);

                _log?.Invoke($"PCdrv: sent {bytesRead} bytes for handle={handle}");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"PCdrv: read error: {ex.Message}");
                SendString("NOPE");
            }
        }

        private void HandleWrite(CancellationToken ct)
        {
            SendString("OKAY");

            int handle = ReadInt32(ct);
            int length = ReadInt32(ct);
            int memAddr = ReadInt32(ct);

            _log?.Invoke($"PCdrv: WRITE handle={handle} len={length}");

            var f = FindOpenFile(handle);
            if (f == null)
            {
                SendString("NOPE");
                return;
            }

            SendString("OKAY");

            // Read data from PSX
            byte[] data = ReadBytes(length, ct);

            f.Stream.Write(data, 0, length);
            f.Stream.Flush();

            SendString("OKAY");
            WriteInt32(length);

            _log?.Invoke($"PCdrv: wrote {length} bytes to handle={handle}");
        }

        private void HandleSeek(CancellationToken ct)
        {
            SendString("OKAY");

            int handle = ReadInt32(ct);
            int offset = ReadInt32(ct);
            int whence = ReadInt32(ct);

            _log?.Invoke($"PCdrv: SEEK handle={handle} offset={offset} whence={whence}");

            var f = FindOpenFile(handle);
            if (f == null)
            {
                SendString("NOPE");
                return;
            }

            SeekOrigin origin = whence switch
            {
                0 => SeekOrigin.Begin,
                1 => SeekOrigin.Current,
                2 => SeekOrigin.End,
                _ => SeekOrigin.Begin
            };

            try
            {
                long newPos = f.Stream.Seek(offset, origin);
                SendString("OKAY");
                WriteInt32((int)newPos);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"PCdrv: seek error: {ex.Message}");
                SendString("NOPE");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // PS1 output routing
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Routes PS1 printf output to PSXConsoleWindow (via _psxLog) if available,
        /// otherwise falls back to the control panel log.
        /// </summary>
        private void EmitPsxLine(string text)
        {
            if (_psxLog != null)
                _psxLog.Invoke(text);
            else
                _log?.Invoke($"PS1> {text}");
        }

        // ═══════════════════════════════════════════════════════════════
        // Chunked data write — matches NOTPSXSerial's WriteBytes()
        // Sends data in 2048-byte chunks; for protocol V2+ Unirom
        // responds with CHEK/MORE/ERR! per chunk.
        // ═══════════════════════════════════════════════════════════════

        private void WriteDataChunked(byte[] data)
        {
            int chunkSize = 2048;
            for (int i = 0; i < data.Length; i += chunkSize)
            {
                int thisChunk = Math.Min(chunkSize, data.Length - i);
                _port.Write(data, i, thisChunk);

                // Wait for bytes to drain
                while (_port.BytesToWrite > 0)
                    Thread.Sleep(0);

                // V2 protocol: wait for CHEK, send chunk checksum, wait for MORE
                // For now, handle this if present
                if (_port.BytesToRead >= 4)
                {
                    string resp = ReadFixedString(4);
                    if (resp == "CHEK")
                    {
                        ulong chunkSum = 0;
                        for (int j = 0; j < thisChunk; j++)
                            chunkSum += data[i + j];
                        _port.Write(BitConverter.GetBytes((uint)chunkSum), 0, 4);
                        Thread.Sleep(1);

                        // Wait for MORE or ERR!
                        string ack = WaitFor4CharResponse(5000);
                        if (ack == "ERR!")
                        {
                            _log?.Invoke("PCdrv: chunk checksum error, retrying...");
                            i -= chunkSize; // retry
                        }
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // File handle helpers
        // ═══════════════════════════════════════════════════════════════

        private int NextHandle() => _files.Count + 1;

        private PCFile FindOpenFile(string name)
        {
            for (int i = 0; i < _files.Count; i++)
            {
                if (!_files[i].Closed && _files[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return _files[i];
            }
            return null;
        }

        private PCFile FindOpenFile(int handle)
        {
            for (int i = 0; i < _files.Count; i++)
            {
                if (!_files[i].Closed && _files[i].Handle == handle)
                    return _files[i];
            }
            return null;
        }

        private string ResolvePath(string filename)
        {
            // Strip leading slashes and backslashes
            filename = filename.TrimStart('/', '\\');
            return Path.Combine(_baseDir, filename);
        }

        // ═══════════════════════════════════════════════════════════════
        // Low-level serial I/O
        // ═══════════════════════════════════════════════════════════════

        private int ReadInt32(CancellationToken ct)
        {
            byte[] buf = new byte[4];
            for (int i = 0; i < 4; i++)
                buf[i] = (byte)ReadByteBlocking(ct);
            return BitConverter.ToInt32(buf, 0);
        }

        private uint ReadUInt32(CancellationToken ct)
        {
            byte[] buf = new byte[4];
            for (int i = 0; i < 4; i++)
                buf[i] = (byte)ReadByteBlocking(ct);
            return BitConverter.ToUInt32(buf, 0);
        }

        private byte[] ReadBytes(int count, CancellationToken ct)
        {
            byte[] data = new byte[count];
            int pos = 0;
            while (pos < count)
            {
                ct.ThrowIfCancellationRequested();
                if (_port.BytesToRead > 0)
                {
                    int read = _port.Read(data, pos, Math.Min(count - pos, _port.BytesToRead));
                    pos += read;
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
            return data;
        }

        private int ReadByteBlocking(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (_port.BytesToRead > 0)
                    return _port.ReadByte();
                Thread.Sleep(1);
            }
            throw new OperationCanceledException();
        }

        private string ReadNullTermString(CancellationToken ct)
        {
            var sb = new StringBuilder();
            while (true)
            {
                int b = ReadByteBlocking(ct);
                if (b == 0) break;
                sb.Append((char)b);
                if (sb.Length > 255) break;
            }
            return sb.ToString();
        }

        private void SendString(string s)
        {
            byte[] data = Encoding.ASCII.GetBytes(s);
            _port.Write(data, 0, data.Length);
        }

        private void WriteInt32(int value)
        {
            _port.Write(BitConverter.GetBytes(value), 0, 4);
        }

        private void WriteUInt32(uint value)
        {
            _port.Write(BitConverter.GetBytes(value), 0, 4);
        }

        private string ReadFixedString(int count)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (_port.BytesToRead > 0)
                    sb.Append((char)_port.ReadByte());
            }
            return sb.ToString();
        }

        private string WaitFor4CharResponse(int timeoutMs)
        {
            string buffer = "";
            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                if (_port.BytesToRead > 0)
                {
                    buffer += (char)_port.ReadByte();
                    if (buffer.Length > 4)
                        buffer = buffer.Substring(buffer.Length - 4);
                    if (buffer == "MORE" || buffer == "ERR!")
                        return buffer;
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
            return buffer;
        }

        private static uint CalculateChecksum(byte[] data)
        {
            // Force V3-style for PCDrv reads (per NOTPSXSerial: forceProtocolV3=true)
            uint sum = 0;
            for (int i = 0; i < data.Length; i++)
                sum += data[i];
            return sum;
        }
    }
}
