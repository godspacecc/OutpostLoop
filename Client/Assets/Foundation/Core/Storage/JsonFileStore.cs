using System;
using System.IO;
using System.Text;
using UnityEngine;
namespace Game
{
    /// <summary>独立 JSON 文件存储；不读取旧项目数据，不自动创建玩家进度。</summary>
    public sealed class JsonFileStore<T> where T : class
    {
        // 格式封套用于区分合法空 JSON 与本存储格式，避免默默接受无关文件。
        [Serializable] private sealed class Envelope { public int format; public T data; }
        private readonly string directory;
        /// <summary>显式目录供测试隔离；默认仅访问本项目持久化子目录。</summary>
        public JsonFileStore(string explicitDirectory = null)
        { directory = Path.GetFullPath(explicitDirectory ?? Path.Combine(Application.persistentDataPath, "OutpostLoop")); }
        private string Resolve(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName == "." || fileName == ".." || fileName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("只允许文件名，禁止路径。", nameof(fileName));
            return Path.Combine(directory, fileName);
        }
        /// <summary>缺失、损坏、未知版本分别返回状态，不自动修复或覆盖。</summary>
        public SaveReadResult<T> Read(string fileName)
        {
            var path = Resolve(fileName);
            if (!File.Exists(path)) return new SaveReadResult<T> { Status = SaveFileStatus.Missing };
            try
            {
                var value = JsonUtility.FromJson<Envelope>(File.ReadAllText(path, Encoding.UTF8));
                if (value == null || value.format == 0 || value.data == null) throw new InvalidDataException("文件缺少有效封套或数据。");
                if (value.format != 1) return new SaveReadResult<T> { Status = SaveFileStatus.Unsupported, Error = "不支持的文件版本。" };
                return new SaveReadResult<T> { Status = SaveFileStatus.Valid, Data = value.data };
            }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is UnauthorizedAccessException)
            { return new SaveReadResult<T> { Status = SaveFileStatus.Corrupted, Error = error.Message }; }
        }
        /// <summary>先完整写入同目录临时文件，再原子替换；异常不会删除旧文件。</summary>
        public void Save(string fileName, T data)
        {
            var path = Resolve(fileName);
            if (data == null) throw new ArgumentNullException(nameof(data));
            var existing = Read(fileName);
            if (existing.Status == SaveFileStatus.Corrupted || existing.Status == SaveFileStatus.Unsupported)
                throw new InvalidDataException("现有文件损坏或版本未知，禁止覆盖。");
            var json = JsonUtility.ToJson(new Envelope { format = 1, data = data });
            Directory.CreateDirectory(directory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = Encoding.UTF8.GetBytes(json); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
