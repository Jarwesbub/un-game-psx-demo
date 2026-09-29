using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    [Icon("Packages/net.psxsplash.splashedit/Icons/LuaFile.png")]
    public class LuaFile : ScriptableObject
    {
        [SerializeField] private string luaScript;
        public string LuaScript => luaScript;

        public void Init(string luaCode)
        {
            luaScript = luaCode;
        }
    }
}
 