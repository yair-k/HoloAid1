﻿using UnityEngine;
using System.Collections;
using System;

namespace Levenshtein
{
    public class LevenshteinDistance
    {
        private const string BaseString = "2l9f2o0l25m4205Gc0353m58c75nc29057n245cnrn290nD0v45";            
        public const int ToleranceLevel = 7;                                                                

        public static int GetLevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s))
            {
                if (string.IsNullOrEmpty(t))
                    return 0;
                return t.Length;
            }

            if (string.IsNullOrEmpty(t))
            {
                return s.Length;
            }

            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; d[i, 0] = i++) ;
            for (int j = 1; j <= m; d[0, j] = j++) ;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {

                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    int min1 = d[i - 1, j] + 1;
                    int min2 = d[i, j - 1] + 1;
                    int min3 = d[i - 1, j - 1] + cost;
                    d[i, j] = Math.Min(Math.Min(min1, min2), min3);             
                }
            }
            return d[n, m];
        }

        public static int GetLevenshteinKey(string s)
        {
            int levenshteinDistance = GetLevenshteinDistance(s, BaseString);
            int key = levenshteinDistance / ToleranceLevel;

            return key;
        }
    }
}