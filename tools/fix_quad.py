"""Rewrite Quad.Draw()'s 3.x device-render-state + Effect.Begin/End block to 4.0
(BlendState + EffectPass.Apply). Content-anchored so indentation doesn't matter."""

f = r'C:\Programming\RotEA26\web\EvilAliensWeb\Game\EvilAliens\Quad.cs'
src = open(f, encoding='utf-8').read()

start_marker = 'BlendFunction blendFunction = graphicsDevice.RenderState.BlendFunction;'
end_marker = 'graphicsDevice.RenderState.BlendFunction = blendFunction;'

start = src.index(start_marker)
end = src.index(end_marker) + len(end_marker)

new_block = (
    "BlendState oldBlend = graphicsDevice.BlendState;\n"
    "\t\t\tgraphicsDevice.BlendState = BlendState.Additive;\n"
    "\t\t\tforeach (EffectPass pass in effect.CurrentTechnique.Passes)\n"
    "\t\t\t{\n"
    "\t\t\t\tpass.Apply();\n"
    "\t\t\t\tgraphicsDevice.DrawUserIndexedPrimitives<VertexPositionNormalTexture>(PrimitiveType.TriangleStrip, vertices, 0, 4, indices, 0, 2);\n"
    "\t\t\t}\n"
    "\t\t\tforeach (EffectPass pass2 in topeffect.CurrentTechnique.Passes)\n"
    "\t\t\t{\n"
    "\t\t\t\tpass2.Apply();\n"
    "\t\t\t\tgraphicsDevice.DrawUserIndexedPrimitives<VertexPositionNormalTexture>(PrimitiveType.TriangleStrip, topvertices, 0, 4, indices, 0, 2);\n"
    "\t\t\t}\n"
    "\t\t\tforeach (EffectPass pass3 in bottomeffect.CurrentTechnique.Passes)\n"
    "\t\t\t{\n"
    "\t\t\t\tpass3.Apply();\n"
    "\t\t\t\tgraphicsDevice.DrawUserIndexedPrimitives<VertexPositionNormalTexture>(PrimitiveType.TriangleStrip, bottomvertices, 0, 4, indices, 0, 2);\n"
    "\t\t\t}\n"
    "\t\t\tgraphicsDevice.BlendState = oldBlend;"
)

src = src[:start] + new_block + src[end:]
open(f, 'w', encoding='utf-8').write(src)
print("Quad.Draw() rewritten for XNA 4.0 (BlendState + EffectPass.Apply)")
