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
  MedPhysio_Free: function (p) {
    _free(p);
  }
});
