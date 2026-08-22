//+------------------------------------------------------------------+
//|                                                    K_iUpDown.mq5 |
//|                                         Copyright Karlis Balcers |
//|                                              http://www.mql5.com |
//+------------------------------------------------------------------+
#property copyright "Karlis Balcers"
#property link      "http://www.mql5.com"
#property version   "1.00"
#property indicator_separate_window
#property indicator_buffers 5
#property indicator_plots   1
#property indicator_type1   DRAW_COLOR_CANDLES
#property indicator_color1  SpringGreen,LimeGreen,Green,DarkGreen,LightSalmon,OrangeRed,Crimson,Maroon

input string comment="Index from 1-10 to increase sensitivity.";
input int tick_volume_index=7;

double ExtOpenBuffer[];
double ExtHighBuffer[];
double ExtLowBuffer[];
double ExtCloseBuffer[];
double ExtColorsBuffer[];
long total_vol=0;
long count_bars=0;
long passNo=0;
//+------------------------------------------------------------------+
//| Custom indicator initialization function                         |
//+------------------------------------------------------------------+
int OnInit()
  {
   SetIndexBuffer(0,ExtOpenBuffer,INDICATOR_DATA);
   SetIndexBuffer(1,ExtHighBuffer,INDICATOR_DATA);
   SetIndexBuffer(2,ExtLowBuffer,INDICATOR_DATA);
   SetIndexBuffer(3,ExtCloseBuffer,INDICATOR_DATA);
   SetIndexBuffer(4,ExtColorsBuffer,INDICATOR_COLOR_INDEX);
   PlotIndexSetInteger(0,PLOT_SHOW_DATA,false);
   IndicatorSetInteger(INDICATOR_DIGITS,_Digits);
   IndicatorSetString(INDICATOR_SHORTNAME,"K_iUpDown");
   total_vol=0;
   count_bars=0;
   return(0);
  }
//+------------------------------------------------------------------+
//| Custom indicator iteration function                              |
//+------------------------------------------------------------------+
int OnCalculate(const int rates_total,      // size of input timeseries
                const int prev_calculated,  // bars handled in previous call
                const datetime& time[],     // Time
                const double& open[],       // Open
                const double& high[],       // High
                const double& low[],        // Low
                const double& close[],      // Close
                const long& tick_volume[],  // Tick Volume
                const long& volume[],       // Real Volume
                const int& spread[])        // Spread
  {

   int  i=0;
   bool vol_up=true;
   passNo++;
   if(i<prev_calculated) i=prev_calculated-1;

   while(i<rates_total)
     {
      if(i!=0)//Do not count the first bar.
        {
         total_vol+=tick_volume[i];
         count_bars++;
        }

      double x=0;
      if(count_bars==0)
        {
         x=tick_volume[i]*100.0/tick_volume_index;
        }
      else
        {
         double avg_vol=1.0*total_vol/count_bars;
         x=tick_volume[i]*100.0/(avg_vol*tick_volume_index);//Get %
        }

      int col=0;
      if(x>0)col=0;
      if(x>25)col=1;
      if(x>50)col=2;
      if(x>75)col=3;

      if(open[i]<close[i])//UP
        {
         ExtColorsBuffer[i]=col;
        }
      else//DOWN
        {
         ExtColorsBuffer[i]=4+col;
        }

      ExtOpenBuffer[i]=0;//open[i];
      ExtHighBuffer[i]=high[i]-open[i];
      ExtLowBuffer[i]=low[i]-open[i];
      ExtCloseBuffer[i]=close[i]-open[i];

      i++;
     }

   return(rates_total);
  }
//+------------------------------------------------------------------+