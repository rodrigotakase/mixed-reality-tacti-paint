using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Bhaptics.SDK2
{
    public class bhaptics_library
    {
        private const string ModuleName = "bhaptics_library";

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool registryAndInit([MarshalAs(UnmanagedType.LPUTF8Str)] string sdkAPIKey, [MarshalAs(UnmanagedType.LPUTF8Str)] string workspaceId, [MarshalAs(UnmanagedType.LPUTF8Str)] string initData);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool registryAndInitHost([MarshalAs(UnmanagedType.LPUTF8Str)] string sdkAPIKey, [MarshalAs(UnmanagedType.LPUTF8Str)] string workspaceId, [MarshalAs(UnmanagedType.LPUTF8Str)] string initData, [MarshalAs(UnmanagedType.LPUTF8Str)] string url);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool wsIsConnected();

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void wsClose();

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int play([MarshalAs(UnmanagedType.LPUTF8Str)] string key, int deviceIndex);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int playParam([MarshalAs(UnmanagedType.LPUTF8Str)] string key, int requestId, float intensity, float duration, float angleX, float offsetY, int deviceIndex);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void playWithoutResult([MarshalAs(UnmanagedType.LPUTF8Str)] string key, int requestId, float intensity, float duration, float angleX, float offsetY, int deviceIndex);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int playWithStartTime([MarshalAs(UnmanagedType.LPUTF8Str)] string key, int requestId, int startMillis, float intensity, float duration, float angleX, float offsetY, int deviceIndex);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int playDot(int requestId, int position, int durationMillis, int[] motors, int size, int deviceIndex);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int playWaveform(int requestId, int position, int[] motorValues, int[] playTimeValues, int[] shapeValues, int repeatCount, int motorLen);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int playWaveformDk3(int requestId, int position, int[] motorValues, int[] playTimeValues, int[] shapeValues, int frequency, int repeatCount, int motorLen);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int playPath(int requestId, int position, int durationMillis, float[] xValues, float[] yValues, int[] intensityValues, int len, int deviceIndex);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int playLoop([MarshalAs(UnmanagedType.LPUTF8Str)] string key, int requestId, float intensity, float duration, float angleX, float offsetY, int interval, int maxCount, int deviceIndex);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int getEventTime([MarshalAs(UnmanagedType.LPUTF8Str)] string eventId);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void pause([MarshalAs(UnmanagedType.LPUTF8Str)] string key);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void resume([MarshalAs(UnmanagedType.LPUTF8Str)] string key);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void stop(int requestKey);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void stopByEventId([MarshalAs(UnmanagedType.LPUTF8Str)] string eventId);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void stopAll();

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool isbHapticsConnected(int position);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool isPlaying();

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool isPlayingByRequestId(int requestId);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool isPlayingByEventId([MarshalAs(UnmanagedType.LPUTF8Str)] string eventId);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr bHapticsGetHapticMessage([MarshalAs(UnmanagedType.LPUTF8Str)] string apiKey, [MarshalAs(UnmanagedType.LPUTF8Str)] string appId, int lastVersion, out int status);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr bHapticsGetHapticMappings([MarshalAs(UnmanagedType.LPUTF8Str)] string apiKey, [MarshalAs(UnmanagedType.LPUTF8Str)] string appId, int lastVersion, out int status);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool isPlayerRunning();

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool isPlayerInstalled();

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool launchPlayer([MarshalAs(UnmanagedType.U1)] bool tryLaunch);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr getDeviceInfoJson();

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr getHapticMappingsJson();

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool ping([MarshalAs(UnmanagedType.LPUTF8Str)] string address);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool pingAll();

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool swapPosition([MarshalAs(UnmanagedType.LPUTF8Str)] string address);

        [DllImport(ModuleName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool setDeviceVsm([MarshalAs(UnmanagedType.LPUTF8Str)] string address, int vsm);

        // https://stackoverflow.com/questions/36239705/serialize-and-deserialize-json-and-json-array-in-unity
        public static List<HapticDevice> GetDevices()
        {
            IntPtr ptr = getDeviceInfoJson();

            var devicesStr = PtrToStringUtf8(ptr);

            if (devicesStr.Length == 0)
            {
                BhapticsLogManager.LogFormat("GetDevices() empty. {0}", devicesStr);
                return new List<HapticDevice>();
            }
            var hapticDevices = JsonUtility.FromJson<DeviceListMessage>("{\"devices\":" + devicesStr + "}");

            return BhapticsHelpers.Convert(hapticDevices.devices);
        }

        private static string PtrToStringUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return "";
            }

            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0)
                len++;
            if (len == 0)
            {
                return "";
            }

            byte[] array = new byte[len];
            Marshal.Copy(ptr, array, 0, len);
            return System.Text.Encoding.UTF8.GetString(array);
        }
    }
}
