﻿using System;

namespace HSMSensorDataObjects.Swagger
{
    [AttributeUsage(AttributeTargets.Property)]
    public class SwaggerExcludeAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum | AttributeTargets.Method)]
    //Use this attribute to hide the entity from swagger doc, if needed.
    //Please note, that the attribute works for the class you use it on. To ignore subtypes, rather add this attribute to them
    //or specify their names in a special dictionary in SwaggerIgnoreFilter.
    public class SwaggerIgnoreAttribute : Attribute
    {
    }
}
