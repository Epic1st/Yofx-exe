//+------------------------------------------------------------------+
//| PatternTypes.mqh                                                  |
//| Shared Wave / Swing Type Definitions                              |
//|                                                                   |
//| This file contains data types shared by the EA, SwingCache,       |
//| and Engine modules. It contains no logic or global variables.     |
//+------------------------------------------------------------------+

#ifndef PATTERN_TYPES_MQH
#define PATTERN_TYPES_MQH

//+------------------------------------------------------------------+
//| Swing Structure                                                   |
//+------------------------------------------------------------------+
#ifndef SWING_STRUCT_DEFINED
#define SWING_STRUCT_DEFINED

struct Swing
{
   int    bar;
   double price;
   char   type;
};

#endif

//+------------------------------------------------------------------+
//| Wave Structure                                                    |
//+------------------------------------------------------------------+
#ifndef WAVE_STRUCT_DEFINED
#define WAVE_STRUCT_DEFINED

struct Wave
{
   double amplitude;
   int    duration;
   char   direction;
   double startPrice;
   double endPrice;
   int    startBar;
   int    endBar;
};

#endif

#endif // PATTERN_TYPES_MQH