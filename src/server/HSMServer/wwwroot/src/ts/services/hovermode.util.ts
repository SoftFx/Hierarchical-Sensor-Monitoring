import {Hovermode, HoverModeEnum} from "../types";

export class HovermodeUtils {
    public static toHovermode(mode: HoverModeEnum): Hovermode{
        switch (mode){
            case HoverModeEnum.X:
                return "x";
            case HoverModeEnum.Y:
                return "y";
            case HoverModeEnum.False:
                return false;
            case HoverModeEnum.Closest:
                return "closest";
            case HoverModeEnum.XUnified:
                return "x unified";
            case HoverModeEnum.YUnified:
                return "y unified";
        }
    }
}