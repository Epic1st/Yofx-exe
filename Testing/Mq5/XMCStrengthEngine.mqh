//+------------------------------------------------------------------+
//|                      XMCStrengthEngine.mqh                        |
//|        Strength calculation + extreme detection + entry symbol   |
//+------------------------------------------------------------------+
#ifndef XMCSTRENGTHENGINE_MQH
#define XMCSTRENGTHENGINE_MQH

class CXMCStrengthEngine
{
private:
   string m_symbols[6];
   double m_strength[6];
   int    m_count;

   double m_strongestValue;
   double m_secondStrongestValue;
   string m_strongestSymbol;

public:

   //--- Initialization
   void Init(string &symbols[], int count)
   {
      m_count = MathMin(count, 6);

      for(int i = 0; i < 6; i++)
      {
         if(i < m_count)
            m_symbols[i] = symbols[i];
         else
            m_symbols[i] = "";

         m_strength[i] = 0.0;
      }

      m_strongestValue       = 0.0;
      m_secondStrongestValue = 0.0;
      m_strongestSymbol      = "";
   }

   //--- Calculate relative gamma strength for all symbols
   void Calculate(int lookback)
   {
      double gamma_values[6];
      ArrayInitialize(gamma_values, 0.0);

      double max_gamma = -999999.0;
      double min_gamma =  999999.0;

      int period = MathMax(lookback / 4, 5);

      for(int i = 0; i < m_count; i++)
      {
         if(m_symbols[i] == "")
         {
            m_strength[i] = 0.0;
            continue;
         }

         if(iBars(m_symbols[i], 0) < period * 2 + 2)
         {
            m_strength[i] = 0.0;
            continue;
         }

         double p0 = iClose(m_symbols[i], 0, 1);
         double p1 = iClose(m_symbols[i], 0, period + 1);
         double p2 = iClose(m_symbols[i], 0, period * 2 + 1);

         if(p0 == 0.0 || p1 == 0.0 || p2 == 0.0)
         {
            m_strength[i] = 0.0;
            continue;
         }

         double delta1 = (p0 - p1) / p1 * 100.0;
         double delta2 = (p1 - p2) / p2 * 100.0;
         double gamma   = delta1 - delta2;

         gamma_values[i] = gamma;

         if(gamma > max_gamma)
            max_gamma = gamma;

         if(gamma < min_gamma)
            min_gamma = gamma;
      }

      double range = max_gamma - min_gamma;

      // Fixed: changed loop variable from 'i' to 'j' to prevent MQL4 redeclaration error
      for(int j = 0; j < m_count; j++)
      {
         if(range > 0.0)
            m_strength[j] = ((gamma_values[j] - min_gamma) / range) * 100.0;
         else
            m_strength[j] = 0.0;
      }
   }

   //--- Find strongest and second-strongest symbols
   void FindExtreme()
   {
      m_strongestValue       = 0.0;
      m_secondStrongestValue = 0.0; // Fixed: was m_secondStrongestVal
      m_strongestSymbol      = "";

      for(int i = 0; i < m_count; i++)
      {
         if(m_symbols[i] == "")
            continue;

         double strength = m_strength[i];

         if(strength <= 1.0)
            continue;

         if(strength > m_strongestValue)
         {
            m_secondStrongestValue = m_strongestValue; // Fixed: was m_secondStrongestVal
            m_strongestValue       = strength;
            m_strongestSymbol      = m_symbols[i];
         }
         else if(strength > m_secondStrongestValue) // Fixed: was m_secondStrongestVal
         {
            m_secondStrongestValue = strength; // Fixed: was m_secondStrongestVal
         }
      }
   }

   //--- Return entry symbol if strength passes threshold and gap
   string GetEntrySymbol(double entryLevel, double confidenceGap)
   {
      if(m_strongestSymbol == "")
         return "";

      if(m_strongestValue >= entryLevel &&
         m_strongestValue - m_secondStrongestValue >= confidenceGap) // Fixed: was m_secondStrongestVal
      {
         return m_strongestSymbol;
      }

      return "";
   }

   //--- Accessors
   double GetStrength(int index)
   {
      // Added bounds check to prevent undefined array access
      if(index < 0 || index >= m_count)
         return 0.0;

      return m_strength[index];
   }

   string GetStrongestSymbol()
   {
      return m_strongestSymbol;
   }

   double GetStrongestValue()
   {
      return m_strongestValue;
   }

   double GetSecondStrongestValue()
   {
      return m_secondStrongestValue; // Fixed: was m_secondStrongestVal
   }
};

#endif // XMCSTRENGTHENGINE_MQH