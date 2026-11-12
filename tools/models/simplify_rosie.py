import trimesh,fast_simplification as fs,numpy as np,sys
src,dst,target=sys.argv[1],sys.argv[2],int(sys.argv[3])
s=trimesh.load(src,force='scene')
tot=sum(len(g.faces) for g in s.geometry.values());frac=target/tot
out=trimesh.Scene()
for name in s.graph.nodes_geometry:
    T,g=s.graph[name];m=s.geometry[g].copy();m.apply_transform(T)
    mat=m.visual.material if hasattr(m.visual,'material') else None
    m.merge_vertices(digits_vertex=5);n=len(m.faces)
    if n>50000:
        parts=[p for p in m.split(only_watertight=False) if len(p.faces)>=24 and np.linalg.norm(p.extents)>0.004]
        sp=[]
        for p in parts:
            w=max(int(len(p.faces)*frac*1.5),200)
            if len(p.faces)>w*1.2:
                try:
                    pv,pf=fs.simplify(p.vertices.astype(np.float32),p.faces.astype(np.int32),target_count=w,agg=9)
                    p=trimesh.Trimesh(pv,pf,process=False)
                except Exception: pass
            sp.append(p)
        m=trimesh.util.concatenate(sp)
    want=max(int(n*frac),min(n,1500))
    v,f=m.vertices.astype(np.float32),m.faces.astype(np.int32)
    for it in range(4):
        if len(f)<=want*1.15: break
        v,f=fs.simplify(v,f,target_count=want,agg=7+it*3)
    m=trimesh.Trimesh(v,f,process=True)
    if mat is not None: m.visual=trimesh.visual.TextureVisuals(material=mat)
    print(name[:30],n,len(m.faces))
    out.add_geometry(m,node_name=name)
print('out faces',sum(len(g.faces) for g in out.geometry.values()))
out.export(dst)
