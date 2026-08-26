using System.Runtime.CompilerServices;

// The emitter's name and type mappings are internal because they are an implementation detail of
// translation, not a public contract. They still have to be held against the runtime interface
// they emit calls into, and that comparison can only be made from an assembly that references
// both — which is this one.
[assembly: InternalsVisibleTo("YO4X.Mql5.Runtime.Tests")]
