⚡ 将光影烘培进贴图的方案 (Bake Light and Shadow into Texture)
==================================================================================================
本文将介绍一种在Unity里将光照、阴影信息烘培进模型贴图的技术（支持 Diffuse 和 Standard (PBR) 材质）

# 一、在开发 Oculus Quest、Pico 或高性能移动端游戏时：
<br>1、如果追求画质：使用 Standard PBR Shader。
<br>✅ 优点：金属反射、菲涅尔效应、AO、自发光一应俱全。
<br>❌ 代价：Draw Call 高，ALU 运算量大，带宽压力爆炸，设备发烫，VR 掉帧。

2、如果追求性能：使用 Unlit (无光照) Shader。
<br>✅ 优点：极致性能，几乎零开销。
<br>❌ 代价：画面**“死平”**，像纸模一样，毫无体积感和材质细节。

挑战：我们能否让物体在运行时使用最廉价的 Unlit Shader，却依然拥有 Standard 材质的金属光泽、柔和阴影和 GI 全局光照，
并且大幅降低 GPU 的 ALU 负载，显著减少设备的发热？

<br>
解决方案：我基于Unity软件开发了一个光影烘培工具。它的核心理念是 “离线计算，运行时作弊”。
通过自定义渲染管线，我们将 Unity 的光照系统（Lightmap）、场景反射（Reflection Probe）以及材质本身的 PBR 属性，
进行数学上的“预合成”，最终输出一张包含所有信息的“超级贴图”，而最后仅使用Unlit Shader来进行渲染，可以做到极低的开销。
<img width="610" height="485" alt="image" src="https://github.com/user-attachments/assets/85fb53d1-f936-4b16-a660-6427cf3a752f" />

# 二. 核心技术：双管齐下的烘焙策略 (Dual-Pipeline)
为了兼顾“非金属”的柔和与“金属”的质感，本工具并未采用单一算法，而是设计了两套完全独立的烘焙管线，专为不同材质设计：

🟢 模式 A：Diffuse (非金属专用)
特点：专治木头、布料、石头等粗糙表面。
采用 Gamma 视觉校正算法，智能提亮暗部细节，模拟人眼对柔和光照的感知，彻底解决传统烘焙“死黑、惨白”的问题。

🔵 模式 B：Standard (金属专用)
特点：专治金属、瓷器、电子屏等高光表面。
黑科技：智能反射捕获：自动寻找最近的 Reflection Probe，把环境倒影“画”在贴图上。
严格遵循 PBR 物理公式，完美还原金属质感，拒绝“塑料感”。
<img width="1378" height="794" alt="image" src="https://github.com/user-attachments/assets/455597d3-e782-4ec3-bb69-f187171b102e" />
<img width="1863" height="909" alt="image" src="https://github.com/user-attachments/assets/5b1096aa-cbab-40fd-87b0-599f355064e3" />



# 三. 最佳应用场景 (Use Cases)
VR 一体机项目 (Quest/Pico)：
对帧率要求极高（72/90 FPS），但又需要展示高质感道具（如枪械、家居）的场景。
移动端开放世界：
海量的远景建筑、路边杂物，烘焙后可配合 GPU Instancing 疯狂降低 Draw Call。
高性能展厅/元宇宙：
需要在 WebGL 或低端机上流畅运行的高画质场景。

Other app show case(only for showing)
<img width="1640" height="1584" alt="image" src="https://github.com/user-attachments/assets/466313f3-b153-4073-9366-d6d574b34f44" />
<img width="2129" height="948" alt="image" src="https://github.com/user-attachments/assets/6adb9cf5-b35a-4fc8-8780-ed2edb8a305b" />



