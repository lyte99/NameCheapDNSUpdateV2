using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;

namespace NameCheapDNSUpdate
{

        [XmlRoot(ElementName = "interface-response")]
        public class Interfaceresponse
        {

            [XmlElement(ElementName = "Command")]
            public string? Command { get; set; }

            [XmlElement(ElementName = "Language")]
            public string? Language { get; set; }

            [XmlElement(ElementName = "IP")]
            public string? IP { get; set; }

            [XmlElement(ElementName = "ErrCount")]
            public int ErrCount { get; set; }

            [XmlElement(ElementName = "errors")]
            public object? Errors { get; set; }

            [XmlElement(ElementName = "ResponseCount")]
            public int ResponseCount { get; set; }

            [XmlElement(ElementName = "responses")]
            public object? Responses { get; set; }

            [XmlElement(ElementName = "Done")]
            public bool Done { get; set; }

            [XmlElement(ElementName = "debug")]
            public object? Debug { get; set; }
        }

}
