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
  MedPhysio_Free: function (p) {
    _free(p);
  }
});
