# 角色素材与地图视野

最新四张原图来自用户已下载到 Downloads 的透明 PNG，并对照引用聊天 6aef251e-fb00-83ea-ad45-fd7bc1ecc7cf 的角色说明核对。read_thread 可读文本不能直接提供那次生成的四个二进制文件，实际使用本机下载原件。已复制到 src/AntMind.Desktop/Assets/Roles，PNG 角点 alpha=0，manifest.json 记录尺寸／朝向／SHA256。只复制、不重绘、不重新生成，不修改原图字节。

- queen：皇冠、披风和米白腹部。
- hauler：绿叶托、绑带和食物。
- scout：护目镜和围巾。
- soldier：暗红头盔、金边和短颚。

RoleArtwork 缓存四张图，RolePortrait 与 WorldCanvas 复用。RenderOptions 为 BitmapInterpolationMode.None。地图普通蚂蚁大小约 1.8 格，蚁后约 3.6 格，大小随缩放同比变化，不设置固定屏幕像素最小／最大尺寸；模型负载和真实接触半径不变。同一半格拥挤区在地图坐标中做临时视觉散开，再通过相机投影，确保间距和图片大小同比缩放，命中测试使用同样的绘制中心。不能把显示散开当作游戏物理位置。

MapCamera 不引用 Avalonia，仅保存相机浮点数；Core／Programs／Contracts 保持无 UI 依赖。MapPresentationCheck 将这份纯相机源文件链接进 Checks 进行坐标和边界检查，无须让 Checks 引用 Desktop UI 程序集。

操作：滚轮以鼠标为锚点缩放；拖动平移；−／+ 以视野中心缩放；全图适配整张地图；蚁后定位巢穴并保留缩放。地图焦点下 F／Home 对应全图／蚁后。百分比以 16 像素／格为 100%，最高 400%，最低为地图适配窗口。

相机只是观察工具，所有世界操作仍经 Runtime actor。全图仍然只显示已发现食物和敌人的合法投影，不提供隐藏总量。正式地图 128×128；初始资源点数和刷新未另行调平衡，未做满人口长时性能测试。当前角色为静态像素形象加水平朝向，不是多方向完整动画。
