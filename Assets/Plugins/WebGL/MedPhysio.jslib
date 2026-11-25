mergeInto(LibraryManager.library, {
  MedPhysio_Ready: function () {
    return (typeof window !== 'undefined' && window.MedPhysio) ? 1 : 0;
  },
  MedPhysio_List: function () {
    return stringToNewUTF8(window.MedPhysio.list());
  },
  MedPhysio_Def: function (id) {
    return stringToNewUTF8(window.MedPhysio.def(UTF8ToString(id)));
  },
  MedPhysio_Create: function (id, values) {
    return window.MedPhysio.create(UTF8ToString(id), UTF8ToString(values));
  },
  MedPhysio_Set: function (h, values) {
    window.MedPhysio.set(h, UTF8ToString(values));
  },
  MedPhysio_Act: function (h, id) {
    window.MedPhysio.act(h, UTF8ToString(id));
  },
  MedPhysio_Step: function (h, dt, maxSamples) {
    return stringToNewUTF8(window.MedPhysio.step(h, dt, maxSamples));
  },
  MedPhysio_Curve: function (h, i) {
    return stringToNewUTF8(window.MedPhysio.curve(h, i));
  },
  // ---- scene mirror: the web version's own 3D scene, drawn by Unity (Tools/physio-bridge/mirror.ts)
  MedPhysio_SceneBuild: function (h) {
    return stringToNewUTF8(window.MedPhysio.sceneBuild(h));
  },
  MedPhysio_SceneDispose: function (h) {
    window.MedPhysio.sceneDispose(h);
  },
  MedPhysio_SceneFrame: function (h, dt) {
    return window.MedPhysio.sceneFrame(h, dt);
  },
  MedPhysio_SceneCopyFrame: function (h, dst, cap) {
    var f = window.MedPhysio.sceneFrameData(h);
    var n = Math.min(f.length, cap);
    HEAPF32.set(n < f.length ? f.subarray(0, n) : f, dst >> 2);
    return f.length;
  },
  MedPhysio_SceneStructure: function (h) {
    return stringToNewUTF8(window.MedPhysio.sceneStructure(h));
  },
  MedPhysio_SceneGeom: function (h, gid, dst, cap) {
    var f = window.MedPhysio.sceneGeom(h, gid);
    if (f.length <= cap) HEAPF32.set(f, dst >> 2);
    return f.length;
  },
  MedPhysio_SceneIndex: function (h, gid, tri, dst, cap) {
    var f = window.MedPhysio.sceneIndex(h, gid, tri);
    if (f.length <= cap) HEAP32.set(f, dst >> 2);
    return f.length;
  },
  MedPhysio_SceneTex: function (h, tid, dst, cap) {
    var f = window.MedPhysio.sceneTex(h, tid);
    if (f.length <= cap) HEAPU8.set(f, dst);
    return f.length;
  },
  MedPhysio_SceneFit: function (h, aspect, fov) {
    return stringToNewUTF8(window.MedPhysio.sceneFit(h, aspect, fov));
  },
  MedPhysio_SceneCam: function (h, x, y, z) {
    window.MedPhysio.sceneCam(h, x, y, z);
  },
  MedPhysio_LodBias: function (b) {
    window.MedPhysio.lodBias(b);
  },
  MedPhysio_Vibrate: function (ms) {
    if (navigator.vibrate) navigator.vibrate(ms);
  },
  MedPhysio_SceneTool: function (h, cmd, arg) {
    return stringToNewUTF8(window.MedPhysio.sceneTool(h, UTF8ToString(cmd), UTF8ToString(arg)) || '');
  },
  // ---- 2D schematic mode (Tools/physio-bridge/view2d.ts)
  MedPhysio_View2dHas: function (id) {
    return (window.MedPhysio && window.MedPhysio.view2dHas) ? window.MedPhysio.view2dHas(UTF8ToString(id)) : 0;
  },
  MedPhysio_View2dOpen: function (h) {
    return window.MedPhysio.view2dOpen(h);
  },
  MedPhysio_View2dRect: function (x, y, w, h) {
    window.MedPhysio.view2dRect(x, y, w, h);
  },
  MedPhysio_View2dFrame: function (h, dt, playing) {
    window.MedPhysio.view2dFrame(h, dt, playing);
  },
  MedPhysio_View2dPoll: function () {
    return (window.MedPhysio && window.MedPhysio.view2dPoll) ? window.MedPhysio.view2dPoll() : 0;
  },
  MedPhysio_Muscle2dOpen: function () {
    return window.MedPhysio.muscle2dOpen();
  },
  MedPhysio_Muscle2dFrame: function (dt) {
    window.MedPhysio.muscle2dFrame(dt);
  },
  MedPhysio_View2dClose: function () {
    if (window.MedPhysio && window.MedPhysio.view2dClose) window.MedPhysio.view2dClose();
  },
  // ---- muscle section (Tools/physio-bridge/muscle.ts)
  MedPhysio_MuscleOpen: function (tab, phone) {
    return window.MedPhysio.muscleOpen(UTF8ToString(tab), phone);
  },
  MedPhysio_MuscleAct: function (cmd, arg, phone) {
    return window.MedPhysio.muscleAct(UTF8ToString(cmd), UTF8ToString(arg), phone);
  },
  MedPhysio_MuscleView: function () {
    return stringToNewUTF8(window.MedPhysio.muscleView());
  },
  MedPhysio_MusclePart: function (id) {
    return stringToNewUTF8(window.MedPhysio.musclePart(UTF8ToString(id)));
  },
  MedPhysio_MuscleChart: function (kind, w, h, dst, cap) {
    var f = window.MedPhysio.muscleChart(UTF8ToString(kind), w, h);
    if (f.length <= cap) HEAPU8.set(f, dst);
    return f.length;
  },
  MedPhysio_MuscleChartLen: function (x, w) {
    return window.MedPhysio.muscleChartLen(x, w);
  },
  MedPhysio_Free: function (p) {
    _free(p);
  }
});
