// ============================================================
// EC Register Map — 从 GCUService.exe 反编译提取
// 总地址: ~120 个 EC 寄存器
// 基础地址指针: 风扇曲线从 0x0F00 开始 (3840)
// ============================================================

namespace CCU.Alternative.EC;

public static class EcRegisterMap
{
    // ═══════════════════════════════════════════════
    // 风扇 & 系统状态 (1000-1200)
    // ═══════════════════════════════════════════════
    public const ushort BIF_DC_BYTE1          = 1026;  // 电池信息 DC 字节1
    public const ushort BIF_DC_BYTE2          = 1027;  // 电池信息 DC 字节2
    public const ushort BIF_DV_BYTE1          = 1032;  // 电池信息 DV 字节1
    public const ushort BIF_DV_BYTE2          = 1033;  // 电池信息 DV 字节2
    public const ushort BST_BPR_BYTE1         = 1076;  // 电池状态 BPR 字节1
    public const ushort BST_BPR_BYTE2         = 1077;  // 电池状态 BPR 字节2
    public const ushort BST_BRC_BYTE1         = 1078;  // 电池状态 BRC 字节1
    public const ushort BST_BRC_BYTE2         = 1079;  // 电池状态 BRC 字节2
    public const ushort BST_BPV_BYTE1         = 1080;  // 电池状态 BPV 字节1
    public const ushort BST_BPV_BYTE2         = 1081;  // 电池状态 BPV 字节2
    public const ushort OEM_EC_VER            = 1108;  // EC 固件版本
    public const ushort SystemID              = 1110;  // 系统识别码
    public const ushort SILENTMODE_STATUS     = 1115;  // 静音模式状态
    public const ushort MAIN_FAN_RPM_BYTE1    = 1124;  // 主风扇转速 低字节
    public const ushort MAIN_FAN_RPM_BYTE2    = 1125;  // 主风扇转速 高字节
    public const ushort BIOS_INFO5            = 1126;  // BIOS 信息5
    public const ushort SECOND_FAN_RPM_BYTE2  = 1131;  // 副风扇转速 高字节
    public const ushort SECOND_FAN_RPM_BYTE1  = 1132;  // 副风扇转速 低字节
    public const ushort BIOSFuncReg           = 1142;  // BIOS 功能寄存器
    public const ushort OEM_SUB_VER2          = 1149;  // OEM 子版本2
    public const ushort PowSource             = 1168;  // 电源 (AC/DC)
    public const ushort BATTERY_ALERT         = 1172;  // 电池警报
    public const ushort BIOS_INFO_3_BYTE      = 1183;  // BIOS 信息3
    public const ushort Bt1Temperature        = 1186;  // 电池温度1
    public const ushort BT1CycleCount_BYTE1   = 1190;  // 电池循环计数 字节1
    public const ushort BT1CycleCount_BYTE2   = 1191;  // 电池循环计数 字节2
    public const ushort Bt1RSOC               = 1195;  // 电池剩余容量

    // ═══════════════════════════════════════════════
    // 电源 / PL1-PL4 / 性能模式 (1798-2010)
    // ═══════════════════════════════════════════════
    public const ushort AP_BIOS_CONTROL       = 1798;  // AP BIOS 控制
    public const ushort AP_OEM_BYTE9          = 1830;  // OEM 字节9
    public const ushort AP_OEM_BYTE10         = 1831;  // OEM 字节10 (自定义灯测试?)
    public const ushort CPU_DOUBLE_FLAG       = 1831;  // CPU 双标志支持
    public const ushort ESHUTTER_STATUS       = 1831;  // 电子快门状态
    public const ushort AP_OEM_BYTEB          = 1832;  // OEM 字节B
    public const ushort GPU_STATUS            = 1834;  // GPU 状态
    public const ushort GAMING_PL1_DEFAULT    = 1840;  // 游戏模式 PL1 默认值
    public const ushort GAMING_PL2_DEFAULT    = 1841;  // 游戏模式 PL2 默认值
    public const ushort GAMING_PL4_DEFAULT    = 1842;  // 游戏模式 PL4 默认值
    public const ushort GAMING_D_DEFAULT      = 1843;  // 游戏模式 D 默认值
    public const ushort OFFICE_PL1_DEFAULT    = 1844;  // 办公模式 PL1 默认值
    public const ushort OFFICE_PL2_DEFAULT    = 1845;  // 办公模式 PL2 默认值
    public const ushort OFFICE_PL4_DEFAULT    = 1846;  // 办公模式 PL4 默认值
    public const ushort OFFICE_D_DEFAULT      = 1847;  // 办公模式 D 默认值
    public const ushort MYFAN3_CPU_TAU        = 1848;  // MyFan3 CPU TAU
    public const ushort PROJECT_ID_BYTE       = 1856;  // 项目 ID
    public const ushort AP_OEM_BYTE           = 1857;  // OEM 字节
    public const ushort FAN_ALERT_BYTE        = 1857;  // 风扇警报
    public const ushort SUPPORT_BYTE5         = 1858;  // 支持字节5
    public const ushort ConfigurableTGP_DB_CTRL  = 1859;  // 可配置 TGP Dynamic Boost 控制
    public const ushort MYFAN2_L1_PWM         = 1859;  // MyFan2 L1 PWM
    public const ushort ConfigurableTGP_VALUE = 1860;  // 可配置 TGP 值
    public const ushort MYFAN2_L2_PWM         = 1860;  // MyFan2 L2 PWM
    public const ushort DB_TotalProcTarget    = 1861;  // Dynamic Boost 总处理目标
    public const ushort MYFAN2_L3_PWM         = 1861;  // MyFan2 L3 PWM
    public const ushort DB_MaxinumTGP_VALUE   = 1862;  // Dynamic Boost 最大 TGP
    public const ushort MYFAN2_L4_PWM         = 1862;  // MyFan2 L4 PWM
    public const ushort MYFAN2_L5_PWM         = 1863;  // MyFan2 L5 PWM
    public const ushort LIGHTBAR_CONTROL      = 1864;  // 灯条控制
    public const ushort REDBAR_CONTROL        = 1865;  // 红灯条控制
    public const ushort GREENBAR_CONTROL      = 1866;  // 绿灯条控制
    public const ushort BLUEBAR_CONTROL       = 1867;  // 蓝灯条控制
    public const ushort OEMSERVICE_PROJECT_ID = 1868;  // OEM 服务项目 ID
    public const ushort BIOS_OEM_BYTE         = 1870;  // BIOS OEM 字节
    public const ushort MAFAN_CONTROL         = 1873;  // 主风扇控制
    public const ushort CPU_VRM_CURR_LIMIT    = 1875;  // CPU VRM 电流限制
    public const ushort CPU_VRM_MAX_CURR      = 1876;  // CPU VRM 最大电流
    public const ushort MAIN_FAN_L_DUTY       = 1883;  // 主风扇左占空比
    public const ushort MAIN_FAN_R_DUTY       = 1884;  // 主风扇右占空比
    public const ushort TRIGGER_BYTE2         = 1885;  // 触发字节2
    public const ushort SUPPORT_BYTE1         = 1893;  // 支持字节1
    public const ushort SUPPORT_BYTE2         = 1894;  // 支持字节2
    public const ushort Light_ChinaMode       = 1894;  // 灯: 中国模式
    public const ushort TRIGGER_BYTE          = 1895;  // 触发字节
    public const ushort STATUS_BYTE           = 1896;  // 状态字节
    public const ushort AP_EC_LOGO_R          = 1897;  // Logo 灯 R
    public const ushort RGBKB_LEVEL_R         = 1897;  // RGB 键盘 R
    public const ushort AP_EC_LOGO_G          = 1898;  // Logo 灯 G
    public const ushort RGBKB_LEVEL_G         = 1898;  // RGB 键盘 G
    public const ushort AP_EC_LOGO_B          = 1899;  // Logo 灯 B
    public const ushort RGBKB_LEVEL_B         = 1899;  // RGB 键盘 B
    public const ushort RGBKB_LEVEL_DEFAULT_R = 1900;  // RGB 键盘默认 R

    // ═══════════════════════════════════════════════
    // 性能模式 / GPU / PL (1923-2024)
    // ═══════════════════════════════════════════════
    public const ushort PL1_SETTING_VALUE     = 1923;  // PL1 设置值
    public const ushort PL2_SETTING_VALUE     = 1924;  // PL2 设置值
    public const ushort MYFAN3_GPU_SETTING    = 1931;  // MyFan3 GPU 设置
    public const ushort AP_EC_LOGO_CONFIRM    = 1932;  // Logo 确认
    public const ushort AP_OEM_BYTE2          = 1932;  // OEM 字节2
    public const ushort L4_PWM_DEFAULT_MYFAN2 = 1932;  // MyFan2 L4 默认
    public const ushort SINGLEKBL_ENABLE      = 1932;  // 单区键盘灯使能
    public const ushort L5_PWM_DEFAULT_MYFAN2 = 1933;  // MyFan2 L5 默认
    public const ushort SINGLEKBL_SUPPORTPOWER = 1934; // 单区键盘灯电源支持
    public const ushort SUPPORT_BYTE6         = 1934;  // 支持字节6
    public const ushort MYFANI_MIN_SPEED      = 1950;  // MyFanI 最小速度
    public const ushort MYFANI_MIN_TEMP       = 1951;  // MyFanI 最小温度
    public const ushort MYFANI_EXTRA_SPEED    = 1952;  // MyFanI 额外速度
    public const ushort BIOS_OEM_BYTE3        = 1955;  // BIOS OEM 字节3
    public const ushort AP_BIOS_BYTE          = 1956;  // AP BIOS 字节
    public const ushort AP_OEM_BYTE3          = 1957;  // OEM 字节3
    public const ushort AP_OEM_BYTE4          = 1958;  // OEM 字节4
    public const ushort BATSAVER_PL1_DEFAULT  = 1959;  // 省电模式 PL1 默认
    public const ushort BATSAVER_PL2_DEFAULT  = 1960;  // 省电模式 PL2 默认
    public const ushort BATSAVER_PL4_DEFAULT  = 1961;  // 省电模式 PL4 默认
    public const ushort BATSAVER_D_DEFAULT    = 1962;  // 省电模式 D 默认
    public const ushort AP_EC_LOGO            = 1963;  // Logo 灯
    public const ushort MyFanCCI_Mode_Index   = 1963;  // MyFanCCI 模式索引
    public const ushort SmartBalance          = 1964;  // 智能平衡
    public const ushort MyFanCCI_Profile1     = 1968;  // MyFanCCI 配置1
    public const ushort MyFanCCI_Profile2     = 1969;  // MyFanCCI 配置2
    public const ushort MyFanCCI_Profile3     = 1970;  // MyFanCCI 配置3
    public const ushort BAT_CHG_LIMIT_UP      = 1977;  // 充电上限
    public const ushort AP_OEM_BYTE5          = 1989;  // OEM 字节5
    public const ushort BatteryLogoEnable     = 1989;  // 电池 Logo 使能
    public const ushort AP_OEM_BYTE6          = 1990;  // OEM 字节6
    public const ushort AP_OEM_BYTE7          = 1991;  // OEM 字节7
    public const ushort CoolingModeECAddress  = 1991;  // ⭐ 冷却模式
    public const ushort AP_OEM_BYTE8          = 1992;  // OEM 字节8
    public const ushort BIOS_OEM_BYTE8        = 1994;  // BIOS OEM 字节8
    public const ushort COMPLEX_POWER_STATUS  = 1996;  // 综合电源状态
    public const ushort BAT_CHG_LIMIT_DOWN    = 2000;  // 充电下限
    public const ushort AP_SSDTempr           = 2001;  // SSD 温度
    public const ushort ModuleID_GPU          = 2002;  // GPU 模块 ID
    public const ushort ModuleID              = 2003;  // 模块 ID
    public const ushort GAMING_TCC_OFFSET     = 2008;  // 游戏模式 TCC 偏移
    public const ushort OFFICE_TCC_OFFSET     = 2009;  // 办公模式 TCC 偏移
    public const ushort TURBO_TCC_OFFSET      = 2010;  // 狂暴模式 TCC 偏移
    public const ushort LC_FAN_VALUE          = 2022;  // 液冷风扇值
    public const ushort LC_PUMP_VALUE         = 2023;  // 液冷水泵值
    public const ushort EC_DEFAULT_MODE       = 2024;  // ⭐ EC 默认模式
    public const ushort PowerLight_LEDPWM     = 2040;  // 电源灯 PWM
    public const ushort CHANGE_PORT_ID_WKD    = 2043;  // 端口变更 ID

    // ═══════════════════════════════════════════════
    // 键盘/灯/色彩 (3328-3410)
    // ═══════════════════════════════════════════════
    public const ushort AP_OEM_SingleZone     = 3328;  // 单区键盘
    public const ushort Color_Calibration_Supp = 3406; // 色彩校准支持
    public const ushort AP_MISCSWITCH2        = 3407;  // 开关2

    // ═══════════════════════════════════════════════
    // 风扇曲线表 — 温度/占空比基址 (3840-4064)
    // 每 16 字节一组 (UP + DOWN + DUTY + 间隙)
    // ═══════════════════════════════════════════════
    public const ushort FAN_TABLE_BASE        = 3840;  // 风扇曲线基址

    // 风扇曲线表1 (F1)
    public const ushort F1_CPU_TEMP_UP0       = 3840;  // CPU 温度上升阈值[0]
    public const ushort F1_CPU_TEMP_DOWN0     = 3856;  // CPU 温度下降阈值[0]
    public const ushort F1_DUTY0              = 3872;  // 占空比[0]
    public const ushort F1_GPU_TEMP_UP0       = 3888;  // GPU 温度上升阈值[0]
    public const ushort F1_GPU_TEMP_DOWN0     = 3904;  // GPU 温度下降阈值[0]
    public const ushort F1_GPU_DUTY0          = 3920;  // GPU 占空比[0]
    public const ushort RAMFAN1P5_STATUS1     = 3933;  // Fan1.5 状态1
    public const ushort RAMFAN1P5_STATUS2     = 3934;  // Fan1.5 状态2
    public const ushort RAMFAN1P5_CTRL        = 3935;  // Fan1.5 控制

    // 风扇曲线表2 (F2)
    public const ushort F2_CPU_TEMP_UP0       = 3920;
    public const ushort F2_CPU_TEMP_DOWN0     = 3936;
    public const ushort F2_DUTY0              = 3952;
    public const ushort F2_GPU_TEMP_UP0       = 3968;
    public const ushort F2_GPU_TEMP_DOWN0     = 3984;

    // 风扇曲线表3 (F3)
    public const ushort F3_CPU_TEMP_UP0       = 4000;
    public const ushort F3_CPU_TEMP_DOWN0     = 4016;
    public const ushort F3_DUTY0              = 4032;
    public const ushort F3_GPU_TEMP_UP0       = 4048;
    public const ushort F3_GPU_TEMP_DOWN0     = 4064;

    /// <summary>
    /// 风扇曲线节点跨度: TEMP_UP[n] = BASE_TEMP_UP0 + n*2, 每节点地址偏移 +2
    /// DUTY[n] = BASE_DUTY0 + n*2, 每节点地址偏移 +2
    /// 最大 16 个节点
    /// </summary>
    public const int FAN_NODE_COUNT = 16;
    public const int FAN_NODE_OFFSET = 2;  // 每个节点地址偏移 2
    public const int FAN_TABLE_STRIDE = 16; // TEMP_UP 到 TEMP_DOWN 偏移 16
}
