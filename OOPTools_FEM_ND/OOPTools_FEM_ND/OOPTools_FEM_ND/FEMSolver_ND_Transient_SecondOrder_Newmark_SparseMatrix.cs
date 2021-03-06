﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.ComponentModel;

using OOPTools_Math;

namespace OOPTools_FEM_ND
{
    [Serializable]
    public class FEMSolver_ND_Transient_SecondOrder_Newmark_SparseMatrix : FEMSolver_ND_Transient_SecondOrder_Newmark
    {
        /// <summary>
        /// Developed by: Mehrdad Negahban
        /// Date: 12/27/2012
        ///
        /// Purpose: Newmark method to solve second order transient N-D problem using sparse matrix model (M d^2U/dt^2  + K U = F)
        /// Comments:   May add artificial damping (+ C dU/dt)
        ///
        /// Date modified:
        /// Modified by:
        /// Comments:
        /// </summary>
        public override void SolveFEMSystem()
        {
            double beta = Newmark_beta;
            double gamma = Newmark_gamma;
            //Set up the load vector and stiffness matrix
            int NDF = Unknowns_NDoF; //Get the size of the unknowns temperature (one per node)
            Vector F = new Vector(NDF); //Make global load vector (old)
            Vector F_new = new Vector(NDF); //Make global load vector (new)
            MatrixSparseLinkedList K = new MatrixSparseLinkedList(NDF); // Make global stiffness matrix
            MatrixSparseLinkedList M = new MatrixSparseLinkedList(NDF); // Make global thermal mass matrix

            //Assemble F, K, C
            int NE = Elements.Length; //Get the number of elements
            for (int i = 0; i < NE; i++)
            {
                Elements[i].CalculateAndAssemble_FeAndKeAndMe(ref F, ref K, ref M);
            }

            MatrixSparseLinkedList CDamping = ArtificialDamping_Factor_M * M + ArtificialDamping_Factor_K * K;
            MatrixSparseLinkedList Mhat = M + (gamma * DTime) * CDamping + (beta * DTime * DTime) * K;
            Mhat.LUDecomposition_Parallel();
            //Adjust for boundary conditions
            int NBE = BoundaryElements.Length;
            for (int i = 0; i < NBE; i++)
            {
                BoundaryElements[i].AdjustF(Times.Values[StartIndex], ref F);
                BoundaryElements[i].AdjustK(Times.Values[StartIndex], ref K);
            }
            //Initial conditions
            Vector u_old = new Vector(Ut[StartIndex]);
            Vector v_old = new Vector(Vt[StartIndex]);
            //Initial acceleration
            Vector a_old = M.SolveLinearSystem(F - K * u_old);

            Node_ND.Set_UnknownForNode(Nodes, u_old);
            StoreSensors(StartIndex);

            double RTime = 0.0D;
            for (int i = StartIndex; i < NumberOfStepsToStore; i++)
            {
                for (int j = 0; j < NumberOfStepsToSkipOutput; j++)
                {
                    RTime += DTime;//Increment to new time
                    Element_ND.Time = RTime; //Give new time to all elements

                    F_new = new Vector(NDF);
                    for (int k = 0; k < NE; k++)
                    {
                        Elements[k].CalculateAndAssemble_Fe(ref F_new); //Calculate new F
                    }
                    //Adjust new F for BC
                    for (int k = 0; k < NBE; k++)
                    {
                        BoundaryElements[k].AdjustF(RTime, ref F_new);
                    }

                    //solve for new values
                    u_old += DTime * v_old + (0.5D * DTime * DTime * (1.0D - 2.0D * beta)) * a_old;
                    for (int k = 0; k < NBE; k++)
                    {
                        BoundaryElements[k].AdjustU(RTime, ref u_old);
                    }

                    v_old += ((1.0D - gamma) * DTime) * a_old;
                    for (int k = 0; k < NBE; k++)
                    {
                        BoundaryElements[k].AdjustV(RTime, ref v_old);
                    }

                    a_old.Values = Mhat.SolveLinearSystemForwardEliminationAndBackSubstitution((F_new - CDamping * v_old - K * u_old).Values);

                    u_old += (beta * DTime * DTime) * a_old;
                    for (int k = 0; k < NBE; k++)
                    {
                        BoundaryElements[k].AdjustU(RTime, ref u_old);
                    }

                    v_old += (gamma * DTime) * a_old;
                    for (int k = 0; k < NBE; k++)
                    {
                        BoundaryElements[k].AdjustV(RTime, ref v_old);
                    }
                }
                Times.Values[i + 1] = RTime;//Store time
                Ut[i + 1] = new Vector(u_old);//Store vector of node displacements
                Vt[i + 1] = new Vector(v_old);//Store vector of node velocity

                Solver_Output_Info.StoreAStep(i + 1, Times.Values[i + 1], Ut[i + 1], Vt[i + 1], DTime, NumberOfStepsToSkipOutput);
                Solver_Output_Info.SaveToFile(Solver_Output_Info.Make_OuptutAddress());

                Node_ND.Set_UnknownForNode(Nodes, u_old);
                StoreSensors(i + 1);
            }
        }
    }
}
