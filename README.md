###     VirtualCamWithChromakey

Show how to remove background (chromakey technique) from one camera and send in to virtual.    

Contains several remove background (chromakey technique) algorythms:   
- RGBChromakeyRemover (supports obly basic color: red, green, blue)
- YCbCrChromakeyRemover (supports any color)
- RGB3DChromakeyRemover (supports any color)
- GrayScaleChromakeyRemover (supports any color)
- ColorMetricChromakeyRemover (supports any color)
- HSVChromakeyRemover (supports any color)

// Entry Point    
dkxce.RealCamToVirtualCamRouter.Route()

YCbCrChromakeyRemover.Test() Results:    

background image:   
<img src="background.jpg"/>         
ovelay image:   
<img src="greenbox.jpg"/>  
removed greenbox image:   
<img src="greenbox_color_replace.jpg"/>  
mixed image:   
<img src="greenbox_with_background.jpg"/> 

Other results [here](https://github.com/dkxce/VirtualCamWithChromakey/tree/main/images)
