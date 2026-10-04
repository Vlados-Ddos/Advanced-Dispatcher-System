// At compact window sizes the inspector/sidebar overlay the canvas. Coordinate
// projection still uses the full canvas; focus and previews use its visible area.
export function mapViewport(renderer) {
  const canvasWidth=Number.isFinite(renderer.width)?renderer.width:0;
  const canvasHeight=Number.isFinite(renderer.height)?renderer.height:0;
  let left=0,right=canvasWidth,top=0,bottom=canvasHeight;
  const root=renderer.root?.getBoundingClientRect?.();
  if(root){
    const pane=(element,visible=true)=>visible&&element?.getBoundingClientRect?.();
    const sidebar=pane(document.querySelector?.('.sidebar'));
    const inspector=pane(document.getElementById('inspector-frame'),document.getElementById('inspector')?.hidden===false);
    for(const [side,rect] of [['left',sidebar],['right',inspector]]){
      if(!rect || rect.width<=0 || rect.bottom<=root.top || rect.top>=root.top+canvasHeight)continue;
      if(rect.width>=canvasWidth*.85 && rect.height<canvasHeight*.85 && rect.top>root.top+1) {
        bottom=Math.max(top,Math.min(bottom,rect.top-root.top));continue;
      }
      if(side==='left' && rect.left<=root.left+1 && rect.right>root.left)left=Math.min(right,Math.max(left,rect.right-root.left));
      if(side==='right' && rect.right>=root.left+renderer.width-1 && rect.left<root.left+renderer.width)right=Math.max(left,Math.min(right,rect.left-root.left));
    }
  }
  return {left,top,width:Math.max(0,right-left),height:Math.max(0,bottom-top)};
}
